"""Sentinel's offline evaluation harness (Phase 07).

Scores recorded scenario runs against versioned scenario packages, compares
Sentinel with the deploy-blame baseline on the same runs, and applies the
promotion gate. Standard library only, so it runs wherever Python 3.12 does.
"""

__all__ = ["packages", "records", "scoring", "report"]
