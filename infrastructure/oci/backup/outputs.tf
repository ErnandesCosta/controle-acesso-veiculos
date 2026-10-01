output "bucket_name" {
  description = "Name of the private protected-backup bucket."
  value       = oci_objectstorage_bucket.backup.name
}

output "backup_dynamic_group_name" {
  description = "Dynamic group used by the backup compute instance."
  value       = oci_identity_dynamic_group.backup_instance.name
}

output "vault_id" {
  description = "OCID of the backup Vault. Treat environment identifiers as operational data."
  value       = oci_kms_vault.backup.id
  sensitive   = true
}

output "key_id" {
  description = "OCID of the customer-managed backup key. Treat environment identifiers as operational data."
  value       = oci_kms_key.backup.id
  sensitive   = true
}

output "retention_locked" {
  description = "Whether an irreversible retention lock time was supplied."
  value       = var.retention_lock_time != null
}

output "absence_alarm_enabled" {
  description = "Whether the missing-backup alarm was created with a Notifications destination."
  value       = nonsensitive(var.alarm_topic_ocid != null)
}
