ALTER TABLE licences ADD COLUMN trial INTEGER NOT NULL DEFAULT 0;
DROP TABLE trials;
DELETE FROM settings WHERE key = 'trial_days';
