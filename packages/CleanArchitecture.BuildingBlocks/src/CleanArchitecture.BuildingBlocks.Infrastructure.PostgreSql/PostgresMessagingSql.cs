namespace CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;

/// <summary>
/// SQL for the messaging tables. The tables live in a <c>messaging</c> schema inside each
/// service's own database, so services never share them.
/// </summary>
internal static class PostgresMessagingSql
{
    // The advisory lock serializes concurrent startups of several instances of one service.
    internal const string CreateSchema = """
        select pg_advisory_xact_lock(hashtext('cleanarchitecture.messaging.schema'));

        create schema if not exists messaging;

        create table if not exists messaging.outbox (
            message_id uuid primary key,
            stream text not null,
            message_type text not null,
            schema_version integer not null,
            occurred_at timestamptz not null,
            producer text not null,
            correlation_id text,
            causation_id text,
            headers jsonb not null,
            payload text not null,
            attempts integer not null default 0,
            next_attempt_at timestamptz not null default now(),
            published_at timestamptz,
            last_error text
        );

        create index if not exists outbox_pending_due
            on messaging.outbox (next_attempt_at)
            where published_at is null;

        create table if not exists messaging.inbox (
            consumer text not null,
            message_id uuid not null,
            message_type text not null,
            received_at timestamptz not null default now(),
            primary key (consumer, message_id)
        );
        """;

    internal const string InsertOutbox = """
        insert into messaging.outbox (
            message_id, stream, message_type, schema_version, occurred_at,
            producer, correlation_id, causation_id, headers, payload)
        values (
            @message_id, @stream, @message_type, @schema_version, @occurred_at,
            @producer, @correlation_id, @causation_id, @headers::jsonb, @payload)
        """;

    internal const string ClaimDue = """
        with due as (
            select message_id
            from messaging.outbox
            where published_at is null and next_attempt_at <= now()
            order by occurred_at, message_id
            limit @batch_size
            for update skip locked)
        update messaging.outbox as outbox
        set next_attempt_at = now() + @lease
        from due
        where outbox.message_id = due.message_id
        returning outbox.message_id, outbox.stream, outbox.message_type, outbox.schema_version,
            outbox.occurred_at, outbox.producer, outbox.correlation_id, outbox.causation_id,
            outbox.headers::text, outbox.payload, outbox.attempts
        """;

    internal const string MarkPublished = """
        update messaging.outbox
        set published_at = now(), last_error = null
        where message_id = @message_id
        """;

    internal const string MarkFailed = """
        update messaging.outbox
        set attempts = attempts + 1, next_attempt_at = now() + @retry_after, last_error = @error
        where message_id = @message_id
        """;

    internal const string OldestPendingAge = """
        select now() - min(occurred_at)
        from messaging.outbox
        where published_at is null
        """;

    internal const string RecordInbox = """
        insert into messaging.inbox (consumer, message_id, message_type)
        values (@consumer, @message_id, @message_type)
        on conflict do nothing
        """;
}
