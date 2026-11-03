# Parent and child categories

The category form keeps the existing category name, generated code, parent,
description and status. Select **None** to create a main category. The parent
picker searches full paths and closes after selection. A live preview shows the
new path and level. Category paths are derived from parent IDs, never stored as
editable text. Three levels are supported, and product registration allows both
parent and leaf categories.

Categories now offers **Tree View** and **Table View**. Both use the same records,
permission-filtered edit/deactivation actions and search/status filters. The table
shows code, full path, directly assigned product count, status and actions. Counts
include inactive products. Used subtrees cannot be moved through normal editing.
Deactivation preserves IDs and links, and requires active children and active
products to be deactivated or reassigned first.

## Manual deployment

Stop the application/workers and back up the database. Manually apply
[055_category_hierarchy_inheritance.sql](../database/migrations/055_category_hierarchy_inheritance.sql)
as PostgreSQL superuser after 054, then restart the updated application. No SQL
was executed against the application database during development. The screen
improvements also work before 055; inherited fields require the migration.

The migration reuses `parent_category_id`, original IDs and codes. It adds
`inherit_parent_fields` and updates the existing attribute-resolution functions.
Before inheritance is enabled, categories already containing any products,
including inactive products and products in other branches, retain their exact
direct configuration with `inherit_parent_fields=false`. This boundary also
stops fields from more distant ancestors. Existing products are not revalidated
against newly inherited requirements. Enabling inheritance on these populated
categories requires a reviewed migration; the normal editor cannot bypass it.

For unused/new categories, applicable ancestor fields and directly assigned
fields are combined by reusable attribute code. The nearest direct assignment
takes precedence when legacy assignments overlap. Settings label inherited
fields with their source and make those controls read-only. Saving settings
stores direct assignments only. Product registration and both product/variant
APIs use the effective fields, including required values, defaults, search policy
and variant axes. Used configurations and their contributing ancestors lock
against incompatible changes. Inherited rules still permit at most 30 fields
and three variant axes. An unsafe existing configuration aborts migration 055
atomically for review.

## Bike setup organization

Only tenants currently using Bike Accessories are organized during migration.
Archived Bike catalogs are organized on their next Bike setup activation. Other
profiles are not reorganized or given Bike categories.

The proposed root is **Bike Parts & Accessories**, with Helmets, Bike Spare Parts,
Lights & Electrical, Guards & Protection, and Riding Accessories underneath.
Missing named subcategories from the implementation brief are added. Existing
Head Lights keeps its existing name and ID. Existing category records are reused
case-insensitively; brand links and product mappings remain attached to those IDs.

Only unused subtrees are moved. Populated, inactive, ambiguous, or overly deep
subtrees are retained and reported with PostgreSQL notices. Review those notices
before assuming every existing category now follows the proposed structure.
There is no automatic product reassignment, deletion, identity replacement, or
schema rewrite. Returning to Bike setup does not recreate flat root duplicates
of categories that have already been nested.

## Verification

Build the MasterDataChecks project, run `--category-hierarchy-checks`, then run
`tests/StockFlow.UiChecks/category-hierarchy-offline.cjs` with the bundled Node.
These use fake repositories/mocked APIs, covering hierarchy validation, profile
isolation, inherited requirements, legacy boundaries, tree/table views, parent
selection, path previews, direct-only schema saves and desktop/mobile widths.
Existing category-attribute, registration and configuration checks also cover
Clothing, Footwear, Bike and Electrical behavior.

After manual deployment use the [read-only checks](../database/queries/category_hierarchy_checks.sql).
Confirm a real inherited-field registration, schema lock, sibling duplicate and
cycle rejection in PostgreSQL. Check product counts and retained category/brand
IDs against the backup. Database triggers and migration behavior have not been
validated by the mocked browser checks.
