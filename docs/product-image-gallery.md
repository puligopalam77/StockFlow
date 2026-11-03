# Multiple product images (053)

Product registration and Product Edit support up to **five JPG, PNG or WebP images**,
up to **3 MB each**. Select several files together or use Add image repeatedly.
The gallery displays a large preview and thumbnails. Click a thumbnail to preview;
Set as main changes the primary image. Remove deletes an image from the pending
selection; changes take effect when Submit/Save succeeds. Closing without saving
preserves stored images.

Both the single-image uploader and gallery now include a visible **Remove image**
button. In Add Product it clears the pending upload and preview. In Product Edit,
click Remove image and then Save to clear the stored upload. Cancel leaves it
unchanged. Single-image removal needs no new migration; multiple-image storage
still requires migration 053. A replacement upload clears the pending removal flag.

The first stored image is mirrored to the existing single-image endpoint used by
stock and billing screens. Existing single images remain unchanged until their
gallery is explicitly edited. Metadata is returned separately from image bytes;
image requests and updates use the parent product's tenant/profile permissions.
Gallery changes save in the same transaction as product registration. Requests that
omit the gallery retain it; an explicit empty gallery removes its uploaded images.

## Apply manually

1. Back up the database and stop StockFlow and background workers. Migration 052
   must already be successfully installed.
2. Open [053_product_image_gallery.sql](../database/migrations/053_product_image_gallery.sql)
   in your database query tool as PostgreSQL superuser. Run the entire file, from
   BEGIN through COMMIT. Application startup does not run this script.
3. If the query session is still in a failed transaction, run `ROLLBACK;` separately
   first. If this migration fails, run ROLLBACK and inspect the first error before
   retrying the whole file.
4. Run [product_image_gallery_verification.sql](../database/queries/product_image_gallery_verification.sql).
   Expect the new column and enabled tenant flag; existing galleries initially have
   zero entries. Existing single-image columns are unchanged.
5. Restart StockFlow and press Ctrl+F5. Before 053 is installed, the app retains its
   existing single-image uploader. Gallery API writes are rejected until enabled.

## Verify in StockFlow

1. Products → Add Product → Media & Specs: upload three images. Expect three
   thumbnails and one large preview. Use Set as main on the second image, then
   remove the third image. Submit the product.
2. Edit that product. Expect the remaining two images in the saved order. Preview
   both, remove one, save, and reopen. Exactly one should remain.
3. Reopen a product with an older single image. It should appear as Main image.
   Add an image and save; the original should remain alongside the new upload.
4. Cancel an image removal and reopen: the stored image should remain. Remove all
   uploaded images, save, and verify the gallery and old main-image endpoint clear.
   An independently configured Image URL remains available until you clear it.
5. Try six images or an image above 3 MB. The form must reject the selection and
   retain the previous valid selection. Server validation also checks image type,
   file signature, count and size.
6. Switch business profile or sign in under another tenant: galleries must follow
   the parent product's visibility. Direct image URLs for inaccessible products
   must not expose their data. Check the existing stock/billing main-image preview.

Validation completed with a clean .NET build, fake-repository API checks and mocked
browser uploads/reordering/removal/reopening. Migration execution, actual PostgreSQL
save/reload and cross-tenant/profile acceptance remain manual.
