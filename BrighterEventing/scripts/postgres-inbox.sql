-- Brighter Eventing - PostgreSQL Inbox table
-- Run this against your Postgres database (e.g. brighter_eventing) before starting the Subscriber.
-- Use Paramore.Brighter.Inbox.Postgres.PostgreSqlInboxBuilder.GetDDL("Inbox") for the exact schema from your Brighter version.
CREATE TABLE IF NOT EXISTS "Inbox" (
    "MessageId" VARCHAR(255) NOT NULL,
    "ReceivedAt" TIMESTAMPTZ(3) NOT NULL,
    "ContextKey" VARCHAR(256) NOT NULL,
    PRIMARY KEY ("MessageId", "ContextKey")
);

COMMENT ON TABLE "Inbox" IS 'Brighter inbox for de-duplication (exactly-once processing)';
