-- Run against stockflow_db after the tables have been created.
INSERT INTO categories (category_name)
VALUES
('Battery'),
('Inverter'),
('Water Purifier'),
('Accessory'),
('Spare Part')
ON CONFLICT (category_name) DO NOTHING;
