# 0003: Python for the companion service and cloud services

Status: accepted (2026-09-28)

## Decision
The local companion service and the Milestone 2 cloud services are written in Python.

## Why
Best tooling for speech-to-text, ML and LLM SDKs. Heavy work stays out of the Unity Editor process, which keeps play mode responsive. The same code runs locally and in cloud workers.
