# Postmortem: Order Sync Outage, 28 October 2026

**Status:** Draft for review
**Author:** Marcus Webb
**Severity:** SEV-2
**Duration:** 3 hours 12 minutes (02:14 to 05:26 UTC)

## Summary

Orders placed through the web shop stopped appearing in the warehouse system for just over three hours. No orders were lost, but 412 orders were delayed and 37 customers contacted support. The cause was a database connection pool exhausted by a runaway retry loop in the order sync worker.

## Impact

- 412 orders reached the warehouse between 3 and 6 hours late.
- 37 support tickets were raised, mostly asking about delivery dates.
- Next-day delivery was missed for 91 orders. The commercial team has offered those customers free shipping on their next order.

## Timeline (UTC)

- **02:14**: The partner's order API begins returning intermittent 503 errors.
- **02:16**: The sync worker retries failed requests immediately, with no back-off. Retries pile up.
- **02:31**: The database connection pool reaches its limit of 100. New sync jobs fail to start.
- **02:40**: The first alert fires, but it is routed to a mailbox nobody monitors overnight.
- **04:52**: A warehouse supervisor phones the on-call engineer after noticing the order queue was empty.
- **05:03**: The on-call engineer restarts the worker. The queue drains slowly because retries continue.
- **05:26**: The partner API recovers and the backlog clears.

## Root cause

The worker treated every failure as retryable and retried at once. When the partner API degraded, each failing job held a connection while it waited and retried, and the pool filled. There was no cap on attempts and no circuit breaker.

## What went well

The orders themselves were safe in the queue, so no data was lost. Once the on-call engineer was reached, the diagnosis took about ten minutes.

## What went badly

The alert went to the wrong place and the on-call rota was not paged. We learned about the outage from a customer-facing team rather than from monitoring. The pool limit had also never been reviewed since the shop launched.

## Action items

| Action | Owner | Due |
|--------|-------|-----|
| Add exponential back-off and a maximum of five attempts to the sync worker | Marcus Webb | 13 Nov |
| Add a circuit breaker around the partner API client | Li Na Zhou | 20 Nov |
| Route the connection pool alert to the paging service | Sam Odhiambo | 6 Nov |
| Review and document the connection pool limits for all workers | Sam Odhiambo | 27 Nov |
| Write a runbook for order sync stalls | Marcus Webb | 27 Nov |
| Ask the commercial team to confirm the free-shipping credit has been applied | Rosa Delgado | 10 Nov |

## Lessons

Retries without limits turn a partner's small outage into our large one. Alerts that do not page a human are not alerts. We should also agree a policy for tolerating partner API failures before the next peak season begins in late November.
