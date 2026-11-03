# Category registration

Category registration is a single form. Fields & Attributes, Variants, the field
editor, step navigation and the separate Save basic details action are removed.
The Categories page also omits the Category Fields & Variants link. Cancel and
Save Category remain in a fixed footer.

The form retains cascading Main Category/Subcategory selectors, an optional third
level, Category Name, Status, Category Code, Display Order, hierarchy preview and
optional Description. Existing selections open their saved record; Add Main
Category and Add Subcategory start new records. Unsaved changes require confirmation
before switching records. Category codes and display order become editable after
manual migration 057; older installations retain automatic codes.

The Main Category dropdown includes Add New Main Category. The Subcategory dropdown
includes Add New Subcategory under the selected active main category. These options
start a new draft and focus the category name, without adding extra header buttons.
Save reloads the current shop's category list and rejects duplicate sibling names
or category codes, including inactive records. Editing excludes the current ID.
If the duplicate check cannot load, saving stops so the user can retry. The existing
submit lock blocks repeated clicks/events while validation or saving is in progress;
server validation and tenant-scoped unique indexes still protect concurrent writes.

Saving submits only category details. It does not submit a configuration object or
replace saved reusable attribute definitions, variant axes, inheritance settings,
or stock defaults. Existing product registration continues to use saved schemas.
The configuration API and separate attribute settings endpoint remain compatible
with existing records. No schema or inventory data is deleted by this UI change.

## Category list

Category Management uses an ordinary grouped table with Category / Subcategory,
Category Code, Status and Actions. Each main category appears once as a section
heading with a Manage action. Its immediate categories form collapsed group rows;
expand a group to see its direct subcategories, using short names without repeated
paths. Expand All and Collapse All act on the group rows. Empty main categories
remain visible as ordinary rows. Counts describe category groups and subcategories;
product/item counts remain removed.

Main Category, Active/Inactive status and text search still filter the list.
Searching or changing a status filter automatically expands the parents of matching
subcategories, retaining parent context. User expansion choices otherwise persist
across filter redraws and data refreshes. View is read-only; Edit and Deactivate
retain existing permissions. The table scrolls horizontally on mobile. Grouping
changes only presentation and makes no database writes.

## Database compatibility

Removing the category configuration UI needs no database migration.
Migration 056 and existing schema/stock defaults remain supported and unchanged.
To enable editable codes and persisted display order, back up and stop the
application/workers, then manually apply
[057_category_registration_details.sql](../database/migrations/057_category_registration_details.sql)
after 056 as PostgreSQL superuser. No migration is executed automatically.

## Validation

Build the MasterDataChecks project and run --category-registration-tab-checks and
--category-hierarchy-checks. Browser checks use the bundled Node executable with
category-registration-tabs-offline.cjs (historical filename, now the basic-only
form regression), category-cascades-offline.cjs, category-list-offline.cjs and
category-hierarchy-offline.cjs. These cover direct create/edit saves, required
fields, failed-save retention, no configuration writes, older migration support,
cascading navigation, permissions, row filters and desktop/mobile widths.
Tests use fake repositories and mocked APIs; PostgreSQL acceptance remains manual.
