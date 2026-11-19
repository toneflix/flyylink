-- Paddle orders of deleted accounts, so a late webhook or a sign-in catch-up doesn't bring their licences back.
CREATE TABLE deleted_orders (
  provider TEXT NOT NULL,
  order_id TEXT NOT NULL,
  deleted_at INTEGER NOT NULL,
  PRIMARY KEY (provider, order_id)
);
