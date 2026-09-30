# 0001: Local first, cloud optional

Status: accepted (2026-09-28)

## Decision
Everything in Milestone 1 runs on the developer's machine. The only network call is to the LLM API for spec drafting. Cloud team mode is a later, opt-in layer.

## Why
The tool started as a workflow tool for individual developers. Capture needs the live Editor anyway, local transcription is fast and private, and a solo developer gets little from a cloud backend. The cloud earns its place for teams: shared review, many parallel agent runners, and patterns across many testers.

## Consequences
Session folders are the interface between local and cloud. Cloud mode uploads the same folder rather than inventing a second format.
