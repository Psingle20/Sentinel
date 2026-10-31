---
service: queue-consumer
---
# queue-consumer runbook

queue-consumer receives order messages from the `orders` queue, books each order and confirms it with order-service. Six workers share the queue; a healthy queue holds a few messages, none older than a second or two, and every message is received once.

## Symptoms

A queue whose oldest message keeps ageing while the others are processed, with one message's receive count rising, has a poison message: something about that message fails on every receive. The consumer logs the same message ID failing again and again.

A queue that stops draining altogether while the consumer logs attempts that gave up on order-service is waiting on order-service. The consumer retries a confirmation at once, without backoff, so a slow order-service sees more calls, not fewer.

Orders booked twice while every message is received once come from duplicate publication upstream. Booking is not idempotent, so each copy is booked.

In the AWS environment orders reach the queue through the `orders-route` EventBridge rule. Orders published but never processed, with the queue empty and nothing failing, never reached the queue: the rule no longer matches the order events, usually because its pattern was changed.

## Remediation

Quarantine a poison message; it can be released back to the queue once it can be processed. Do not roll the consumer back for it: every revision fails on the same message.

When order-service is slow, address order-service. Rolling the consumer back does not help, because every revision retries the same way.

Duplicate bookings need responders: stop the duplicate publication upstream and reverse the extra bookings. No queue action removes them.

Restore a drifted route to its approved pattern. Do not roll the consumer back: no revision receives what the route does not deliver.
