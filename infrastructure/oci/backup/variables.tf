variable "region" {
  description = "OCI region that hosts the protected backup resources."
  type        = string
}

variable "tenancy_ocid" {
  description = "OCID of the tenancy where the dynamic group and IAM policies are created."
  type        = string
  sensitive   = true
}

variable "backup_compartment_ocid" {
  description = "OCID of the compartment dedicated to protected production backups."
  type        = string
  sensitive   = true
}

variable "backup_instance_ocid" {
  description = "OCID of the compute instance allowed to publish backups and health metrics."
  type        = string
  sensitive   = true
}

variable "bucket_name" {
  description = "Private Object Storage bucket name. Do not include personal or confidential data."
  type        = string
  default     = "controle-acesso-veiculos-backups"

  validation {
    condition     = can(regex("^[A-Za-z0-9._-]+$", var.bucket_name))
    error_message = "bucket_name may contain only letters, numbers, periods, underscores, and hyphens."
  }
}

variable "resource_prefix" {
  description = "Non-confidential prefix used for OCI resource names."
  type        = string
  default     = "controle-acesso-veiculos"

  validation {
    condition     = can(regex("^[a-z0-9-]+$", var.resource_prefix))
    error_message = "resource_prefix may contain only lowercase letters, numbers, and hyphens."
  }
}

variable "recovery_group_name" {
  description = "Existing OCI IAM group allowed to read protected backup objects for approved recovery."
  type        = string
}

variable "key_custodian_group_name" {
  description = "Existing OCI IAM group responsible for Vault key governance."
  type        = string
}

variable "auditor_group_name" {
  description = "Existing OCI IAM group allowed to inspect configuration, alarms, and audit events."
  type        = string
}

variable "retention_lock_time" {
  description = "Optional RFC3339 time after which the 35-day retention rule becomes irreversible. Keep null until homologation succeeds."
  type        = string
  default     = null
  nullable    = true

  validation {
    condition     = var.retention_lock_time == null || can(timecmp(var.retention_lock_time, timestamp()))
    error_message = "retention_lock_time must be null or a valid RFC3339 timestamp."
  }
}

variable "alarm_topic_ocid" {
  description = "OCID of an existing Notifications topic with approved recipients. Null leaves the absence alarm disabled until recipients are configured."
  type        = string
  default     = null
  nullable    = true
  sensitive   = true
}

variable "freeform_tags" {
  description = "Non-confidential tags applied to supported OCI resources."
  type        = map(string)
  default = {
    application = "controle-acesso-veiculos"
    managed-by  = "terraform"
  }
}
