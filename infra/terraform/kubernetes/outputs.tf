output "url" {
  value = "https://${var.hostname}"
}
output "namespace" {
  value = kubernetes_namespace_v1.incidentlens.metadata[0].name
}
