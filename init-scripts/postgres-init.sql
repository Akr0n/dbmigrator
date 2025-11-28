-- PostgreSQL Initialization Script
-- Create test schema and tables with BYTEA for binary data

CREATE SCHEMA IF NOT EXISTS migration_test;

-- Users Table
CREATE TABLE migration_test.users (
    id SERIAL PRIMARY KEY,
    username VARCHAR(50) NOT NULL UNIQUE,
    email VARCHAR(100) NOT NULL,
    password_hash BYTEA,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Products Table (with images as bytea)
CREATE TABLE migration_test.products (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    price DECIMAL(10, 2),
    image BYTEA,
    thumbnail BYTEA,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Orders Table
CREATE TABLE migration_test.orders (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES migration_test.users(id),
    product_id INTEGER NOT NULL REFERENCES migration_test.products(id),
    quantity INTEGER NOT NULL,
    total_price DECIMAL(10, 2),
    order_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    status VARCHAR(50) DEFAULT 'pending'
);

-- Audit Log Table (with binary data for signatures)
CREATE TABLE migration_test.audit_log (
    id SERIAL PRIMARY KEY,
    table_name VARCHAR(100),
    operation VARCHAR(10),
    record_id INTEGER,
    changed_by VARCHAR(100),
    change_data BYTEA,
    change_timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Insert Sample Data
INSERT INTO migration_test.users (username, email, password_hash) VALUES
    ('alice', 'alice@example.com', '\x6861736865645f70617373776f72645f31'::bytea),
    ('bob', 'bob@example.com', '\x6861736865645f70617373776f72645f32'::bytea),
    ('charlie', 'charlie@example.com', '\x6861736865645f70617373776f72645f33'::bytea),
    ('diana', 'diana@example.com', '\x6861736865645f70617373776f72645f34'::bytea);

INSERT INTO migration_test.products (name, description, price, image, thumbnail) VALUES
    ('Laptop Pro', 'High-performance laptop', 1299.99, 
        '\x89504e470d0a1a0a0000000d49484452'::bytea,
        '\x89504e470d0a1a0a0000000d49484452'::bytea),
    ('Wireless Mouse', 'Ergonomic wireless mouse', 29.99,
        '\x89504e470d0a1a0a0000000d49484452'::bytea,
        '\x89504e470d0a1a0a0000000d49484452'::bytea),
    ('USB-C Hub', 'Multi-port USB-C hub', 49.99,
        '\x89504e470d0a1a0a0000000d49484452'::bytea,
        '\x89504e470d0a1a0a0000000d49484452'::bytea),
    ('Mechanical Keyboard', 'RGB mechanical keyboard', 149.99,
        '\x89504e470d0a1a0a0000000d49484452'::bytea,
        '\x89504e470d0a1a0a0000000d49484452'::bytea),
    ('Monitor 4K', '27-inch 4K monitor', 399.99,
        '\x89504e470d0a1a0a0000000d49484452'::bytea,
        '\x89504e470d0a1a0a0000000d49484452'::bytea);

INSERT INTO migration_test.orders (user_id, product_id, quantity, total_price, status) VALUES
    (1, 1, 1, 1299.99, 'completed'),
    (1, 2, 2, 59.98, 'completed'),
    (2, 3, 1, 49.99, 'pending'),
    (2, 4, 1, 149.99, 'shipped'),
    (3, 1, 1, 1299.99, 'processing'),
    (3, 5, 2, 799.98, 'pending'),
    (4, 2, 3, 89.97, 'completed'),
    (4, 4, 1, 149.99, 'completed');

INSERT INTO migration_test.audit_log (table_name, operation, record_id, changed_by, change_data) VALUES
    ('users', 'INSERT', 1, 'system', '\x7b226e616d65223a22416c696365227d'::bytea),
    ('products', 'INSERT', 1, 'system', '\x7b226e616d65223a224c6170746f7020506f72227d'::bytea),
    ('orders', 'INSERT', 1, 'system', '\x7b2273746174757320223a20226f70656e227d'::bytea),
    ('orders', 'UPDATE', 1, 'system', '\x7b2273746174757320223a20226f726465726564227d'::bytea);
