variable "kubeconfig_path" {
  description = "Existing, trusted kubeconfig; this module does not create a cluster."
  type = string
  default = "~/.kube/config"
}
variable "namespace" {
  type = string
  default = "incidentlens"
}
variable "hostname" {
  description = "DNS host routed through your TLS-enabled ingress controller."
  type = string
}
variable "tls_secret_name" {
  description = "Pre-existing Kubernetes TLS secret in the namespace."
  type = string
  default = "incidentlens-tls"
}
variable "oidc_authority" {
  description = "HTTPS OIDC issuer; not a client secret."
  type = string
  validation {
    condition = startswith(var.oidc_authority, "https://")
    error_message = "OIDC authority must use HTTPS."
  }
}
variable "oidc_audience" {
  type = string
}
variable "role_claim_type" {
  description = "JWT role claim emitted by your provider."
  type = string
  default = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
}
variable "database_secret_name" {
  description = "Existing Secret with postgres-connection key; never put DB passwords in Terraform variables/state."
  type = string
  default = "incidentlens-secrets"
}
variable "api_image" {
  description = "Published image. Pin a digest before production release."
  type = string
}
variable "web_image" {
  description = "Published image. Pin a digest before production release."
  type = string
}
variable "ingress_class" {
  type = string
  default = "nginx"
}

variable "oidc_client_id" {
  type = string
  description = "Public OAuth SPA client ID; NEVER a client secret."
}
variable "oidc_scope" {
  type = string
  default = "openid profile"
}
