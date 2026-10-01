data "oci_objectstorage_namespace" "backup" {
  compartment_id = var.tenancy_ocid
}

resource "oci_kms_vault" "backup" {
  compartment_id = var.backup_compartment_ocid
  display_name   = "${var.resource_prefix}-backup-vault"
  vault_type     = "DEFAULT"
  freeform_tags  = var.freeform_tags

  lifecycle {
    prevent_destroy = true
  }
}

resource "oci_kms_key" "backup" {
  compartment_id      = var.backup_compartment_ocid
  display_name        = "${var.resource_prefix}-backup-key"
  management_endpoint = oci_kms_vault.backup.management_endpoint
  protection_mode     = "HSM"
  freeform_tags       = var.freeform_tags

  key_shape {
    algorithm = "AES"
    length    = 32
  }

  is_auto_rotation_enabled = true

  auto_key_rotation_details {
    rotation_interval_in_days = 90
  }

  lifecycle {
    prevent_destroy = true
  }
}

resource "oci_objectstorage_bucket" "backup" {
  compartment_id        = var.backup_compartment_ocid
  namespace             = data.oci_objectstorage_namespace.backup.namespace
  name                  = var.bucket_name
  access_type           = "NoPublicAccess"
  kms_key_id            = oci_kms_key.backup.id
  is_bucket_key_enabled = true
  object_events_enabled = true
  storage_tier          = "Standard"
  versioning            = "Disabled"
  freeform_tags         = var.freeform_tags

  retention_rules {
    display_name = "retain-backups-for-35-days"

    duration {
      time_amount = 35
      time_unit   = "DAYS"
    }

    time_rule_locked = var.retention_lock_time
  }

  lifecycle {
    prevent_destroy = true
  }
}

resource "oci_objectstorage_object_lifecycle_policy" "backup" {
  namespace = data.oci_objectstorage_namespace.backup.namespace
  bucket    = oci_objectstorage_bucket.backup.name

  rules {
    action      = "DELETE"
    is_enabled  = true
    name        = "delete-after-protected-window"
    target      = "objects"
    time_amount = 36
    time_unit   = "DAYS"
  }

  rules {
    action      = "ABORT"
    is_enabled  = true
    name        = "abort-incomplete-uploads"
    target      = "multipart-uploads"
    time_amount = 1
    time_unit   = "DAYS"
  }
}

resource "oci_identity_dynamic_group" "backup_instance" {
  compartment_id = var.tenancy_ocid
  name           = "${replace(var.resource_prefix, "-", "_")}_backup_instance"
  description    = "Compute instance authorized to publish protected database backups."
  matching_rule  = "instance.id = '${var.backup_instance_ocid}'"
  freeform_tags  = var.freeform_tags
}

resource "oci_identity_policy" "backup_runtime" {
  compartment_id = var.tenancy_ocid
  name           = "${var.resource_prefix}-backup-runtime"
  description    = "Least-privilege upload and monitoring permissions for the backup instance."

  statements = [
    "Allow dynamic-group ${oci_identity_dynamic_group.backup_instance.name} to read buckets in compartment id ${var.backup_compartment_ocid}",
    "Allow dynamic-group ${oci_identity_dynamic_group.backup_instance.name} to manage objects in compartment id ${var.backup_compartment_ocid} where all {target.bucket.name='${oci_objectstorage_bucket.backup.name}', any {request.permission='OBJECT_CREATE', request.permission='OBJECT_INSPECT'}}",
    "Allow dynamic-group ${oci_identity_dynamic_group.backup_instance.name} to use metrics in compartment id ${var.backup_compartment_ocid}"
  ]
}

resource "oci_identity_policy" "object_storage_key_usage" {
  compartment_id = var.tenancy_ocid
  name           = "${var.resource_prefix}-object-storage-key-usage"
  description    = "Allows regional Object Storage to use only the backup encryption key."

  statements = [
    "Allow service objectstorage-${var.region} to use keys in compartment id ${var.backup_compartment_ocid} where target.key.id='${oci_kms_key.backup.id}'"
  ]
}

resource "oci_identity_policy" "backup_recovery" {
  compartment_id = var.tenancy_ocid
  name           = "${var.resource_prefix}-backup-recovery"
  description    = "Read-only access to protected backups for the approved recovery group."

  statements = [
    "Allow group ${var.recovery_group_name} to read buckets in compartment id ${var.backup_compartment_ocid}",
    "Allow group ${var.recovery_group_name} to read objects in compartment id ${var.backup_compartment_ocid} where target.bucket.name='${oci_objectstorage_bucket.backup.name}'"
  ]
}

resource "oci_identity_policy" "key_custody" {
  compartment_id = var.tenancy_ocid
  name           = "${var.resource_prefix}-backup-key-custody"
  description    = "Vault and key governance for the designated custodians."

  statements = [
    "Allow group ${var.key_custodian_group_name} to read vaults in compartment id ${var.backup_compartment_ocid}",
    "Allow group ${var.key_custodian_group_name} to manage keys in compartment id ${var.backup_compartment_ocid}"
  ]
}

resource "oci_identity_policy" "backup_audit" {
  compartment_id = var.tenancy_ocid
  name           = "${var.resource_prefix}-backup-audit"
  description    = "Read-only visibility into backup configuration and operational evidence."

  statements = [
    "Allow group ${var.auditor_group_name} to inspect object-family in compartment id ${var.backup_compartment_ocid}",
    "Allow group ${var.auditor_group_name} to read audit-events in compartment id ${var.backup_compartment_ocid}",
    "Allow group ${var.auditor_group_name} to read metrics in compartment id ${var.backup_compartment_ocid}",
    "Allow group ${var.auditor_group_name} to read alarms in compartment id ${var.backup_compartment_ocid}"
  ]
}

resource "oci_monitoring_alarm" "missing_backup" {
  count = var.alarm_topic_ocid == null ? 0 : 1

  compartment_id        = var.backup_compartment_ocid
  metric_compartment_id = var.backup_compartment_ocid
  namespace             = "controle_acesso_veiculos"
  display_name          = "${var.resource_prefix}-missing-daily-backup"
  body                  = "No successful protected database backup was reported within 26 hours. Follow the recovery runbook."
  destinations          = [var.alarm_topic_ocid]
  is_enabled            = true
  query                 = "BackupSuccess[1h]{bucketName = \"${oci_objectstorage_bucket.backup.name}\"}.groupBy(bucketName).absent(26h)"
  severity              = "CRITICAL"
  pending_duration      = "PT1M"
  resolution            = "1h"
  freeform_tags         = var.freeform_tags
}
