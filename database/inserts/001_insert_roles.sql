-- Run against stockflow_db after the tables have been created.
INSERT INTO roles (role_name, description)
VALUES
('Admin', 'Full system access'),
('Sales', 'Sales and customer operations'),
('Service', 'Warranty and service operations')
ON CONFLICT (role_name) DO NOTHING;
