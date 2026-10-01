# Protected PostgreSQL backups on OCI

This Terraform root prepares the protected destination defined by Issue #311. It
creates a private Object Storage bucket, a customer-managed Vault key, a
compute-instance dynamic group, separated IAM policies, a 35-day retention rule,
automatic deletion after the protected window, and an optional absence alarm.

It does **not** provision the application VM, PostgreSQL, human IAM groups, or a
Notifications subscription. Those resources have institutional owners and must
be reviewed in the target tenancy.

## Safety decisions

- The bucket is explicitly `NoPublicAccess` and has versioning disabled because
  OCI does not allow active retention rules and versioning together.
- Objects are protected for 35 days. Lifecycle deletion begins after 36 days to
  avoid attempting deletion on the retention boundary, which OCI evaluates in
  UTC and processes asynchronously.
- The retention rule is initially **unlocked**. Set `retention_lock_time` only
  after a successful homologation upload, expiry test, isolated restore, and
  review by two authorized people. A locked rule cannot be shortened or removed.
- Terraform `prevent_destroy` guards the bucket, Vault, and key against an
  accidental destroy plan. Removal requires an explicit reviewed code change;
  direct OCI permissions must remain equally restricted.
- The backup VM receives only object creation/inspection and metric publication.
  It cannot read, delete, or administer the protected copies.
- Recovery, key custody, and audit use existing, separate human groups. Human
  accounts must be individual and protected by MFA.
- The Terraform state contains OCI identifiers and must use an approved remote
  backend with restricted access. Never commit state, plans, credentials, or real
  `tfvars` files.

## Prerequisites

1. Terraform 1.12 or later and the OCI provider version selected by the lock
   file.
2. An approved OCI compartment and compute instance.
3. Existing groups for recovery, key custody, and audit.
4. An administrator allowed to create Vault, Object Storage, dynamic-group, IAM,
   lifecycle, and Monitoring resources.
5. OCI authentication supplied outside the repository. Provisioning may use an
   individual federated administrator or OCI Resource Manager; the runtime VM
   uses instance principal.
6. An OCI Notifications topic with approved recipients before enabling the
   absence alarm.

## Prepare and review

From this directory:

```bash
cp terraform.tfvars.example terraform.tfvars
terraform init
terraform fmt -check
terraform validate
terraform plan -out=tfplan
terraform show tfplan
```

Replace every example identifier locally. Keep `retention_lock_time = null` and
`alarm_topic_ocid = null` during the first review. A real `terraform.tfvars` and
the generated `tfplan` are ignored by the repository-wide Terraform patterns.

The plan must show:

- `NoPublicAccess`, customer-managed KMS encryption, and no versioning;
- exactly one 35-day retention rule without a lock time;
- deletion after 36 days and abortion of incomplete multipart uploads;
- the dynamic group matching only the approved VM OCID;
- upload-only runtime permissions and separate human responsibilities;
- no unexpected destroy or replacement action.

Apply only after a second authorized person approves the plan:

```bash
terraform apply tfplan
```

## Publish a verified backup from the OCI VM

The production scheduler first creates the dump and manifest in a directory
readable only by the backup service account. It then runs:

```bash
./Publish-PostgreSqlBackupToOci.sh \
  --backup /var/lib/controle-acesso-backup/controle-acesso-<timestamp>.dump \
  --bucket controle-acesso-veiculos-backups \
  --compartment-id <monitoring-compartment-ocid>
```

The script requires Bash, Python 3, GNU core utilities, and OCI CLI. It always
passes `--auth instance_principal`; it neither reads an OCI user profile nor
accepts a credential argument. Before upload it validates the local SHA-256
manifest. Both objects use a unique prefix, reject overwrites, and enable OCI
CLI checksum verification. The uploaded dump is then inspected for the expected
size before the `BackupSuccess` custom metric is emitted. A failed execution
attempts to emit `BackupFailure`; if the VM cannot reach Monitoring, the absence
of `BackupSuccess` still activates the configured alarm.

Do not enable a scheduler until the VM service account, protected local staging
directory, log redaction, Notifications recipients, and isolated restore
procedure have been reviewed in homologation.

## Homologation gates

The infrastructure is not production-ready merely because `terraform apply`
succeeds. Record evidence of all of the following in Issue #311 without exposing
OCIDs, credentials, dump contents, or personal data:

1. bucket privacy and KMS key assignment;
2. upload from the VM with `--auth instance_principal` and checksum verification;
3. denial of object reads and deletes by the backup VM;
4. authorized download by the recovery group;
5. isolated restoration and application-level verification;
6. a successful custom `BackupSuccess` metric and a controlled absence alarm;
7. lifecycle behavior with fictitious homologation objects;
8. approval by two authorized people before setting a future
   `retention_lock_time` and applying the irreversible change.

Until these gates are complete, the PR must use `Refs #311`, the Issue remains
open, and the local dump procedure remains a technical rehearsal rather than a
production backup.

## Authoritative references

- [OCI: calling services from an instance](https://docs.oracle.com/en-us/iaas/Content/Identity/Tasks/callingservicesfrominstances.htm)
- [OCI: securing Object Storage](https://docs.oracle.com/en-us/iaas/Content/Security/Reference/objectstorage_security.htm)
- [OCI: retention rules](https://docs.oracle.com/en-us/iaas/Content/Object/Tasks/usingretentionrules.htm)
- [OCI: lifecycle policies](https://docs.oracle.com/en-us/iaas/Content/Object/Tasks/usinglifecyclepolicies.htm)
- [OCI: creating an absence alarm](https://docs.oracle.com/en-us/iaas/Content/Monitoring/Tasks/create-alarm-absence.htm)
