USE master
GO

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'TestDB')
BEGIN
    CREATE DATABASE TestDB
END
GO

USE TestDB
GO

IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'migration_test')
BEGIN
    EXEC sp_executesql N'CREATE SCHEMA migration_test'
END
GO

CREATE TABLE migration_test.users (
    id INT IDENTITY(1,1) PRIMARY KEY,
    username VARCHAR(50) NOT NULL UNIQUE,
    email VARCHAR(100) NOT NULL,
    password_hash VARBINARY(MAX),
    created_at DATETIME2 DEFAULT GETUTCDATE(),
    updated_at DATETIME2 DEFAULT GETUTCDATE()
)
GO

CREATE TABLE migration_test.products (
    id INT IDENTITY(1,1) PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    price DECIMAL(10, 2),
    image VARBINARY(MAX),
    thumbnail VARBINARY(MAX),
    created_at DATETIME2 DEFAULT GETUTCDATE()
)
GO

CREATE TABLE migration_test.orders (
    id INT IDENTITY(1,1) PRIMARY KEY,
    user_id INT NOT NULL REFERENCES migration_test.users(id),
    product_id INT NOT NULL REFERENCES migration_test.products(id),
    quantity INT NOT NULL,
    total_price DECIMAL(10, 2),
    order_date DATETIME2 DEFAULT GETUTCDATE(),
    status VARCHAR(50) DEFAULT 'pending'
)
GO

CREATE TABLE migration_test.audit_log (
    id INT IDENTITY(1,1) PRIMARY KEY,
    table_name VARCHAR(100),
    operation VARCHAR(10),
    record_id INT,
    changed_by VARCHAR(100),
    change_data VARBINARY(MAX),
    change_timestamp DATETIME2 DEFAULT GETUTCDATE()
)
GO

INSERT INTO migration_test.users (username, email, password_hash) VALUES
('alice', 'alice@example.com', CONVERT(VARBINARY(MAX), 'hashed_password_1')),
('bob', 'bob@example.com', CONVERT(VARBINARY(MAX), 'hashed_password_2')),
('charlie', 'charlie@example.com', CONVERT(VARBINARY(MAX), 'hashed_password_3')),
('diana', 'diana@example.com', CONVERT(VARBINARY(MAX), 'hashed_password_4'))
GO

INSERT INTO migration_test.products (name, description, price, image, thumbnail) VALUES
('Laptop Pro', 'High-performance laptop', 1299.99, CONVERT(VARBINARY(MAX), 'PNG_IMAGE_1'), CONVERT(VARBINARY(MAX), 'THUMB_1')),
('Wireless Mouse', 'Ergonomic wireless mouse', 29.99, CONVERT(VARBINARY(MAX), 'PNG_IMAGE_2'), CONVERT(VARBINARY(MAX), 'THUMB_2')),
('USB-C Hub', 'Multi-port USB-C hub', 49.99, CONVERT(VARBINARY(MAX), 'PNG_IMAGE_3'), CONVERT(VARBINARY(MAX), 'THUMB_3')),
('Mechanical Keyboard', 'RGB mechanical keyboard', 149.99, CONVERT(VARBINARY(MAX), 'PNG_IMAGE_4'), CONVERT(VARBINARY(MAX), 'THUMB_4')),
('Monitor 4K', '27-inch 4K monitor', 399.99, CONVERT(VARBINARY(MAX), 'PNG_IMAGE_5'), CONVERT(VARBINARY(MAX), 'THUMB_5'))
GO

INSERT INTO migration_test.orders (user_id, product_id, quantity, total_price, status) VALUES
(1, 1, 1, 1299.99, 'completed'),
(1, 2, 2, 59.98, 'completed'),
(2, 3, 1, 49.99, 'pending'),
(2, 4, 1, 149.99, 'shipped'),
(3, 1, 1, 1299.99, 'processing'),
(3, 5, 2, 799.98, 'pending'),
(4, 2, 3, 89.97, 'completed'),
(4, 4, 1, 149.99, 'completed')
GO

INSERT INTO migration_test.audit_log (table_name, operation, record_id, changed_by, change_data) VALUES
('users', 'INSERT', 1, 'system', CONVERT(VARBINARY(MAX), 'data1')),
('products', 'INSERT', 1, 'system', CONVERT(VARBINARY(MAX), 'data2')),
('orders', 'INSERT', 1, 'system', CONVERT(VARBINARY(MAX), 'data3')),
('orders', 'UPDATE', 1, 'system', CONVERT(VARBINARY(MAX), 'data4'))
GO
