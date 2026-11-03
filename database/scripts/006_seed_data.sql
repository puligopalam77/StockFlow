-- Run after scripts 001-005. Existing role and category names are skipped.

INSERT INTO roles (role_name, description)
VALUES
('Admin', 'Full system access'),
('Sales', 'Sales and customer operations'),
('Service', 'Warranty and service operations')
ON CONFLICT (role_name) DO NOTHING;

INSERT INTO categories (category_name)
VALUES
('Battery'),
('Inverter'),
('Water Purifier'),
('Accessory'),
('Spare Part')
ON CONFLICT (category_name) DO NOTHING;
