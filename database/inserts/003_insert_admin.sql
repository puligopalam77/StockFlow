-- Execute manually in StockFlow_DB. Existing admin accounts are left unchanged.
-- Run against the database containing the Phase 2 tables, after 001_insert_roles.sql.
-- Generate a hash: dotnet run --project src/StockFlow.Web -- --hash-password
-- Prepared for username admin with the requested initial application password. Only its hash is stored.
DO $$
DECLARE
    admin_hash TEXT := 'AQAAAAIAAYagAAAAEI2pmMv6tXrEZPX8FuAVSitZgopTaHPLMd7NE78r5La+3amXBPneKAruNiWxlE3Mxg==';
    admin_role_id INT;
BEGIN
    IF admin_hash = 'PASTE_GENERATED_HASH_HERE' THEN
        RAISE EXCEPTION 'Generate and paste the password hash before running this script.';
    END IF;
    SELECT role_id INTO admin_role_id FROM roles WHERE role_name = 'Admin' AND is_active;
    IF admin_role_id IS NULL THEN
        RAISE EXCEPTION 'An active Admin role is required. Run 001_insert_roles.sql first.';
    END IF;
    INSERT INTO users (role_id, full_name, username, password_hash, is_active)
    VALUES (admin_role_id, 'Store Owner', 'admin', admin_hash, TRUE)
    ON CONFLICT (username) DO NOTHING;
END $$;
