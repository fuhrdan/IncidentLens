terraform {
  required_version = ">= 1.6.0"
  required_providers {
    kubernetes = { source = "hashicorp/kubernetes", version = "~> 2.32" }
  }
}
provider "kubernetes" {
  config_path = pathexpand(var.kubeconfig_path)
}
