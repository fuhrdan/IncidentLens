# Reliability automation

IncidentLens turns stored service-health samples into reviewable operational signals without bypassing human incident command.

## Burn policy

Each service objective defines an availability target, owning team, acknowledgement and resolution targets, monthly error budget, and two burn thresholds. The evaluator compares recent observed error with the error allowed by the SLO:

`burn rate = (100 - observed availability) / (100 - target availability)`

The fast window covers 60 minutes and catches acute failures. The slow window covers 360 minutes and catches sustained degradation. A signal is created when its window exceeds the configured threshold. Fingerprints combine service, window, and UTC hour so repeat evaluations update the same signal instead of creating alert storms.

## Signal lifecycle

Signals begin as `Open`, or `Suppressed` when evaluation occurs during active maintenance. A commander can acknowledge an open signal, preserving the actor and timestamp, then promote an open or acknowledged signal into incident response. Promotion creates a normal incident and links it to the signal. The incident inherits the signal's suggested severity, service, objective owner team, and burn summary.

Maintenance suppression remains visible as evidence. Suppressed signals cannot be promoted, preventing planned work from accidentally paging the response workflow while preserving the observed SLO data.

## Concurrency and authorization

Objectives, maintenance windows, and signals carry application-managed versions. Every write submits the version the operator viewed. Stale writes return the current record with `409 Conflict`. Viewer roles may inspect policy and signals; only commanders may evaluate, edit policy, schedule maintenance, acknowledge, or promote.
