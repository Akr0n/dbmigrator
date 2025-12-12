-- Oracle Database Initialization Script
-- Create test user, schema and tables with BLOB for binary data

-- Create test user
ALTER SESSION SET "_ORACLE_SCRIPT"=true;

CREATE USER migration_test IDENTIFIED BY "oraclepass123";
GRANT CONNECT, RESOURCE, UNLIMITED TABLESPACE TO migration_test;

-- Connect as migration_test user (commands will run in their schema)
CONNECT migration_test/oraclepass123;

-- Users Table
CREATE TABLE users (
    id NUMBER PRIMARY KEY,
    username VARCHAR2(50) NOT NULL UNIQUE,
    email VARCHAR2(100) NOT NULL,
    password_hash BLOB,
    created_at TIMESTAMP DEFAULT SYSDATE,
    updated_at TIMESTAMP DEFAULT SYSDATE
);

-- Create sequence for users
CREATE SEQUENCE users_seq START WITH 1 INCREMENT BY 1;

-- Products Table (with images as BLOB)
CREATE TABLE products (
    id NUMBER PRIMARY KEY,
    name VARCHAR2(255) NOT NULL,
    description CLOB,
    price NUMBER(10, 2),
    image BLOB,
    thumbnail BLOB,
    created_at TIMESTAMP DEFAULT SYSDATE
);

-- Create sequence for products
CREATE SEQUENCE products_seq START WITH 1 INCREMENT BY 1;

-- Orders Table
CREATE TABLE orders (
    id NUMBER PRIMARY KEY,
    user_id NUMBER NOT NULL REFERENCES users(id),
    product_id NUMBER NOT NULL REFERENCES products(id),
    quantity NUMBER NOT NULL,
    total_price NUMBER(10, 2),
    order_date TIMESTAMP DEFAULT SYSDATE,
    status VARCHAR2(50) DEFAULT 'pending'
);

-- Create sequence for orders
CREATE SEQUENCE orders_seq START WITH 1 INCREMENT BY 1;

-- Audit Log Table (with binary data for signatures)
CREATE TABLE audit_log (
    id NUMBER PRIMARY KEY,
    table_name VARCHAR2(100),
    operation VARCHAR2(10),
    record_id NUMBER,
    changed_by VARCHAR2(100),
    change_data BLOB,
    change_timestamp TIMESTAMP DEFAULT SYSDATE
);

-- Create sequence for audit_log
CREATE SEQUENCE audit_log_seq START WITH 1 INCREMENT BY 1;

-- Insert Sample Data
INSERT INTO users (id, username, email, password_hash) VALUES
    (users_seq.NEXTVAL, 'alice', 'alice@example.com', UTL_RAW.CAST_TO_RAW('hashed_password_1'));

INSERT INTO users (id, username, email, password_hash) VALUES
    (users_seq.NEXTVAL, 'bob', 'bob@example.com', UTL_RAW.CAST_TO_RAW('hashed_password_2'));

INSERT INTO users (id, username, email, password_hash) VALUES
    (users_seq.NEXTVAL, 'charlie', 'charlie@example.com', UTL_RAW.CAST_TO_RAW('hashed_password_3'));

INSERT INTO users (id, username, email, password_hash) VALUES
    (users_seq.NEXTVAL, 'diana', 'diana@example.com', UTL_RAW.CAST_TO_RAW('hashed_password_4'));

INSERT INTO products (id, name, description, price, image, thumbnail) VALUES
    (products_seq.NEXTVAL, 'Laptop Pro', 'High-performance laptop', 1299.99,
    UTL_RAW.CAST_TO_RAW('PNG_IMAGE_DATA_1'), UTL_RAW.CAST_TO_RAW('PNG_THUMB_1'));

INSERT INTO products (id, name, description, price, image, thumbnail) VALUES
    (products_seq.NEXTVAL, 'Wireless Mouse', 'Ergonomic wireless mouse', 29.99,
    UTL_RAW.CAST_TO_RAW('PNG_IMAGE_DATA_2'), UTL_RAW.CAST_TO_RAW('PNG_THUMB_2'));

INSERT INTO products (id, name, description, price, image, thumbnail) VALUES
    (products_seq.NEXTVAL, 'USB-C Hub', 'Multi-port USB-C hub', 49.99,
    UTL_RAW.CAST_TO_RAW('PNG_IMAGE_DATA_3'), UTL_RAW.CAST_TO_RAW('PNG_THUMB_3'));

INSERT INTO products (id, name, description, price, image, thumbnail) VALUES
    (products_seq.NEXTVAL, 'Mechanical Keyboard', 'RGB mechanical keyboard', 149.99,
    UTL_RAW.CAST_TO_RAW('PNG_IMAGE_DATA_4'), UTL_RAW.CAST_TO_RAW('PNG_THUMB_4'));

INSERT INTO products (id, name, description, price, image, thumbnail) VALUES
    (products_seq.NEXTVAL, 'Monitor 4K', '27-inch 4K monitor', 399.99,
    UTL_RAW.CAST_TO_RAW('PNG_IMAGE_DATA_5'), UTL_RAW.CAST_TO_RAW('PNG_THUMB_5'));

INSERT INTO orders (id, user_id, product_id, quantity, total_price, status) VALUES
    (orders_seq.NEXTVAL, 1, 1, 1, 1299.99, 'completed');

INSERT INTO orders (id, user_id, product_id, quantity, total_price, status) VALUES
    (orders_seq.NEXTVAL, 1, 2, 2, 59.98, 'completed');

INSERT INTO orders (id, user_id, product_id, quantity, total_price, status) VALUES
    (orders_seq.NEXTVAL, 2, 3, 1, 49.99, 'pending');

INSERT INTO orders (id, user_id, product_id, quantity, total_price, status) VALUES
    (orders_seq.NEXTVAL, 2, 4, 1, 149.99, 'shipped');

INSERT INTO orders (id, user_id, product_id, quantity, total_price, status) VALUES
    (orders_seq.NEXTVAL, 3, 1, 1, 1299.99, 'processing');

INSERT INTO orders (id, user_id, product_id, quantity, total_price, status) VALUES
    (orders_seq.NEXTVAL, 3, 5, 2, 799.98, 'pending');

INSERT INTO orders (id, user_id, product_id, quantity, total_price, status) VALUES
    (orders_seq.NEXTVAL, 4, 2, 3, 89.97, 'completed');

INSERT INTO orders (id, user_id, product_id, quantity, total_price, status) VALUES
    (orders_seq.NEXTVAL, 4, 4, 1, 149.99, 'completed');

INSERT INTO audit_log (id, table_name, operation, record_id, changed_by, change_data) VALUES
    (audit_log_seq.NEXTVAL, 'users', 'INSERT', 1, 'system', UTL_RAW.CAST_TO_RAW('{"name":"Alice"}'));

INSERT INTO audit_log (id, table_name, operation, record_id, changed_by, change_data) VALUES
    (audit_log_seq.NEXTVAL, 'products', 'INSERT', 1, 'system', UTL_RAW.CAST_TO_RAW('{"name":"Laptop Pro"}'));

INSERT INTO audit_log (id, table_name, operation, record_id, changed_by, change_data) VALUES
    (audit_log_seq.NEXTVAL, 'orders', 'INSERT', 1, 'system', UTL_RAW.CAST_TO_RAW('{"status": "open"}'));

INSERT INTO audit_log (id, table_name, operation, record_id, changed_by, change_data) VALUES
    (audit_log_seq.NEXTVAL, 'orders', 'UPDATE', 1, 'system', UTL_RAW.CAST_TO_RAW('{"status": "ordered"}'));

COMMIT;
