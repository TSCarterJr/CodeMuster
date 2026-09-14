## Stage 1: Record permitted use
**Goal**: Replace MIT with personal and internal company use terms, per Tim's request.
**Success Criteria**: License and decision record distinguish use from resale and explain the idea limitation.
**Tests**: Review license against the requested permissions and restrictions.
**Status**: Complete

## Stage 2: Ship consistent license metadata
**Goal**: Include the license in npm packages and .NET package metadata.
**Success Criteria**: Every staged npm package carries the root license and references it.
**Tests**: Failing-then-passing staging test using all six platform fixtures.
**Status**: Complete

## Stage 3: Verify and document
**Goal**: Verify local packaging and record the task status.
**Success Criteria**: Local checks pass; publication status is explicit.
**Tests**: npm tests, dotnet test, diff checks.
**Status**: Complete

Local verification: all 16 npm tests, full dotnet test suite, and git diff --check passed on 2026-09-14. The packaging test first failed on the missing LICENSE, then passed for the launcher and all six platforms. Staging/production publication has not been performed; retain this plan until release verification.
