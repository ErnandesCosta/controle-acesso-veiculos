terraform {
  required_version = ">= 1.12.0, < 2.0.0"

  required_providers {
    oci = {
      source  = "oracle/oci"
      version = "~> 9.3"
    }
  }
}

provider "oci" {
  region = var.region
}
