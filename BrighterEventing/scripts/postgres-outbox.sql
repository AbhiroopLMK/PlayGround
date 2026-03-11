-- Brighter Eventing - PostgreSQL Outbox table
-- Run this against your Postgres database (e.g. brighter_eventing) before starting the Publisher.
-- Alternatively, the Publisher will attempt to create the table on startup using Brighter's DDL.

-- Generated from Paramore.Brighter.Outbox.PostgreSql.PostgreSqlOutboxBuilder.GetDDL("Outbox")
CREATE TABLE IF NOT EXISTS "Outbox" (
    "MessageId" VARCHAR(255) NOT NULL,
    "Topic" VARCHAR(255) NOT NULL,
    "MessageType" VARCHAR(32) NOT NULL,
    "Timestamp" TIMESTAMPTZ(3) NOT NULL,
    "CorrelationId" VARCHAR(255) NULL,
    "ReplyTo" VARCHAR(255) NULL,
    "ContentType" VARCHAR(128) NULL,
    "PartitionKey" VARCHAR(255) NULL,
    "WorkflowId" VARCHAR(255) NULL,
    "JobId" VARCHAR(255) NULL,
    "Dispatched" TIMESTAMPTZ(3) NULL,
    "HeaderBag" TEXT NOT NULL,
    "Body" TEXT NOT NULL,
    "Source" VARCHAR(255) NULL,
    "Type" VARCHAR(255) NULL,
    "DataSchema" VARCHAR(255) NULL,
    "Subject" VARCHAR(255) NULL,
    "TraceParent" VARCHAR(255) NULL,
    "TraceState" VARCHAR(255) NULL,
    "Baggage" TEXT NULL,
    "Created" TIMESTAMPTZ(3) NOT NULL DEFAULT NOW(),
    "CreatedID" INT NOT NULL GENERATED ALWAYS AS IDENTITY,
    UNIQUE("CreatedID"),
    PRIMARY KEY ("MessageId")
);

COMMENT ON TABLE "Outbox" IS 'Brighter transactional outbox for guaranteed at-least-once publishing';
