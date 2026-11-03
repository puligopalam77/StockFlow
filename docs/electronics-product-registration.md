# Electronic product registration

Customers remain under Configuration. Products now configures item pricing, model barcodes, specifications and variants inside the Product Registration dialog. The separate Variants & labels submenu and automatic redirect after saving have been removed. The legacy variant URL and APIs remain available for compatibility and existing label tools.

For one model, leave Has Variants off and enter its SKU, purchase/selling prices, optional manufacturer model barcode, low-stock threshold and specifications. For multiple capacities or ratings, enable Has Variants and enter each configuration in the same form. Examples:

| Item | Suggested specifications |
| --- | --- |
| Battery | Capacity: 150 Ah; Voltage: 12 V; Technology: tubular |
| Inverter | Rating: 1100 VA; Input voltage: 12 V; Waveform: sine wave |
| Geyser | Tank capacity: 25 L; Power: 2000 W; Mounting: vertical |

Specification names and values are flexible; existing clothing attributes also remain usable. New registrations default to serial tracking. Select quantity tracking for items without individual serial numbers. Existing products keep their configured tracking type, and used products cannot change tracking. Serial numbers belong to the physical items and are entered on purchase receipt, rather than in the catalog. Warranty duration and provider use the existing warranty fields; existing variants retain their own tax/warranty values unless edited explicitly.

Saving product metadata and edited/new configurations uses one database transaction. A rejected SKU, barcode or variant rolls back the entire save. Registration never adds stock. Existing variant IDs and internal barcode identifiers are retained. Saved configurations cannot be removed from this form; their identities and historical links remain intact. Manufacturer model codes and individual unit serials remain distinct.

Permissions are unchanged: product CREATE/EDIT authorizes metadata; PRODUCT_VARIANTS VIEW plus CREATE/EDIT authorizes the corresponding configurations. Deactivation/reactivation requires the existing variant deactivation permission. Users without variant editing permission can continue metadata edits without overwriting configuration values. Store minimum overrides are retained.

No new migration is required. Existing migrations through 027 provide the schema. The optional `database/queries/electronics_registration_checks.sql` is read-only; it changes neither records nor permissions. No SQL was applied to the running application database.

Validation: build and Configuration/navigation checks; disposable PostgreSQL checks for combined saves, transaction rollback, stable generated barcodes and unchanged stock; browser checks for conditional fields, specifications, failure retention, reopening saved variants and desktop/mobile layouts.
