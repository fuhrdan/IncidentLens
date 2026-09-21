# This reference deploys into an EXISTING cluster with an ingress controller,
# external PostgreSQL, OIDC provider, TLS secret, and database Secret.
# Terraform deliberately does not handle database passwords: it stores secrets
# in state if you pass them through variables or kubernetes_secret resources.
resource "kubernetes_namespace_v1" "incidentlens" {
  metadata { name = var.namespace }
}
resource "kubernetes_config_map_v1" "config" {
  metadata {
    name = "incidentlens-config"
    namespace = kubernetes_namespace_v1.incidentlens.metadata[0].name
  }
  data = {
    oidc-authority = var.oidc_authority
    oidc-audience = var.oidc_audience
    oidc-client-id = var.oidc_client_id
    oidc-scope = var.oidc_scope
    role-claim-type = var.role_claim_type
  }
}
resource "kubernetes_deployment_v1" "api" {
  metadata {
    name = "api"
    namespace = kubernetes_namespace_v1.incidentlens.metadata[0].name
  }
  spec {
    replicas = 1 # Required: outbox dispatcher has no distributed lease.
    strategy { type = "Recreate" } # Never overlap API versions during migrations.
    selector { match_labels = { app = "incidentlens-api" } }
    template {
      metadata { labels = { app = "incidentlens-api" } }
      spec {
        termination_grace_period_seconds = 45
        container {
          name = "api"
          image = var.api_image
          image_pull_policy = "IfNotPresent"
          port {
            name = "http"
            container_port = 8080
          }
          env {
            name = "ASPNETCORE_ENVIRONMENT"
            value = "Production"
          }
          env {
            name = "ASPNETCORE_URLS"
            value = "http://+:8080"
          }
          env {
            name = "Database__Provider"
            value = "PostgreSql"
          }
          env {
            name = "ReverseProxy__RedirectToHttps"
            value = "false"
          }
          env {
            name = "Retention__AllowPurge"
            value = "false"
          }
          env {
            name = "Authentication__RoleClaimType"
            value_from {
              config_map_key_ref {
                name = kubernetes_config_map_v1.config.metadata[0].name
                key = "role-claim-type"
              }
            }
          }
          env {
            name = "ConnectionStrings__PostgreSql"
            value_from {
              secret_key_ref {
                name = var.database_secret_name
                key = "postgres-connection"
              }
            }
          }
          env {
            name = "Authentication__Authority"
            value_from {
              config_map_key_ref {
                name = kubernetes_config_map_v1.config.metadata[0].name
                key = "oidc-authority"
              }
            }
          }
          env {
            name = "Authentication__ClientId"
            value_from {
              config_map_key_ref {
                name = kubernetes_config_map_v1.config.metadata[0].name
                key = "oidc-client-id"
              }
            }
          }
          env {
            name = "Authentication__Scope"
            value_from {
              config_map_key_ref {
                name = kubernetes_config_map_v1.config.metadata[0].name
                key = "oidc-scope"
              }
            }
          }
          env {
            name = "Authentication__Audience"
            value_from {
              config_map_key_ref {
                name = kubernetes_config_map_v1.config.metadata[0].name
                key = "oidc-audience"
              }
            }
          }
          startup_probe {
            http_get {
              path = "/health/live"
              port = "http"
            }
            period_seconds = 5
            failure_threshold = 60
          }
          readiness_probe {
            http_get {
              path = "/health/ready"
              port = "http"
            }
            period_seconds = 10
            timeout_seconds = 4
          }
          liveness_probe {
            http_get {
              path = "/health/live"
              port = "http"
            }
            period_seconds = 20
          }
          resources {
            requests = { cpu = "100m", memory = "192Mi" }
            limits = { cpu = "1", memory = "1Gi" }
          }
          security_context {
            allow_privilege_escalation = false
            run_as_non_root = true
            capabilities { drop = ["ALL"] }
            seccomp_profile { type = "RuntimeDefault" }
          }
        }
      }
    }
  }
}
resource "kubernetes_service_v1" "api" {
  metadata {
    name = "api"
    namespace = kubernetes_namespace_v1.incidentlens.metadata[0].name
  }
  spec {
    selector = { app = "incidentlens-api" }
    port {
      name = "http"
      port = 8080
      target_port = "http"
    }
  }
}
resource "kubernetes_deployment_v1" "web" {
  metadata {
    name = "web"
    namespace = kubernetes_namespace_v1.incidentlens.metadata[0].name
  }
  spec {
    replicas = 1
    selector { match_labels = { app = "incidentlens-web" } }
    template {
      metadata { labels = { app = "incidentlens-web" } }
      spec {
        container {
          name = "web"
          image = var.web_image
          image_pull_policy = "IfNotPresent"
          port {
            name = "http"
            container_port = 8080
          }
          readiness_probe {
            http_get {
              path = "/health/ready"
              port = "http"
            }
            period_seconds = 10
          }
          liveness_probe {
            http_get {
              path = "/health/live"
              port = "http"
            }
            period_seconds = 30
          }
          resources {
            requests = { cpu = "25m", memory = "64Mi" }
            limits = { cpu = "250m", memory = "256Mi" }
          }
          security_context {
            allow_privilege_escalation = false
            capabilities { drop = ["ALL"] }
            seccomp_profile { type = "RuntimeDefault" }
          }
        }
      }
    }
  }
}
resource "kubernetes_service_v1" "web" {
  metadata {
    name = "web"
    namespace = kubernetes_namespace_v1.incidentlens.metadata[0].name
  }
  spec {
    selector = { app = "incidentlens-web" }
    port {
      name = "http"
      port = 8080
      target_port = "http"
    }
  }
}
resource "kubernetes_ingress_v1" "web" {
  metadata {
    name = "incidentlens"
    namespace = kubernetes_namespace_v1.incidentlens.metadata[0].name
    annotations = {
      "nginx.ingress.kubernetes.io/ssl-redirect" = "true"
      "nginx.ingress.kubernetes.io/force-ssl-redirect" = "true"
    }
  }
  spec {
    ingress_class_name = var.ingress_class
    tls {
      hosts = [var.hostname]
      secret_name = var.tls_secret_name
    }
    rule {
      host = var.hostname
      http {
        path {
          path = "/"
          path_type = "Prefix"
          backend {
            service {
              name = kubernetes_service_v1.web.metadata[0].name
              port { name = "http" }
            }
          }
        }
      }
    }
  }
}
