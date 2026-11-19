# Database

The schema is `db/EMarketDb_schema.sql`. It follows SRS section 6: 35 tables in 9 groups plus the ASP.NET Identity role tables, with:

- `uniqueidentifier` keys (sequential defaults), UTC times (`sysutcdatetime()`), `decimal(18,2)` money.
- `CHECK` constraints for statuses, prices, stock, quantities, ratings and percentages (DB-3, DB-4).
- Foreign keys with `NO ACTION` except the cascades the SRS allows (DB-14), and an index on every foreign key.
- Unique indexes: email, shop name and slug, coupon code, order number, payment transaction id, one review per order item, refresh and email token hashes, SKU per product.
- `rowversion` on Order, Product, ProductVariant, Payment, Refund and Payout (DB-15).
- The order number comes from the sequence `OrderNumberSeq` (DB-6).
- Insert-only triggers on `LedgerEntry`, `AuditLog` and `OrderStatusHistory` (DB-11).
- Seed data: roles, six categories, the system settings, 8 divisions, 64 districts and a sample of areas.

Two columns were added to `SellerApplication` because FR-3.2 asks for them and the data dictionary lists none: `BusinessType` and `TradeLicencePath`.

Rules a `CHECK` cannot express are in the business layer: two category levels, a SKU unique in the whole shop, refunds of a payment not above the payment, the payout minimum, at most 20 cart lines.

`AppDbContext` is mapped to this script. Soft-deleted users, shops, products and orders are hidden by a global query filter (DB-10). `CreatedAt` and `UpdatedAt` are set by the data layer, because the script has no trigger for them.
