---
change_id: refactor-opportunities
title: Rank refactor opportunities from the match-flow technical debt
status: preparing
created: 2026-11-04
updated: 2026-11-04
archived_at: null
---

## Notes

Intent: we have an analysis of this repository that documents technical debt and structural risks: context/changes/match-flow-analysis/research.md. This change answers the question that analysis deliberately left open: WHICH of these problems are worth fixing, in what target shape and in what order. We explore every recorded problem in the code and in history, then order them as refactor opportunities.
The change runs in stages: exploration → decision and plan → implementation. During exploration no refactor happens and no decision is made.
Exploration output: this change's research.md, ending with a ranking of options with trade-offs. The owner reads the report first; the decision on what to do is made at the planning stage, and the refactor starts only according to the accepted plan.
