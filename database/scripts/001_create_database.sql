-- Run once against a maintenance database such as postgres, outside a transaction.
-- PostgreSQL does not support CREATE DATABASE IF NOT EXISTS.
-- Skip this script if stockflow_db already exists; run scripts 002-006 in that database.
CREATE DATABASE stockflow_db;
