# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Instructors prepare laboratory exercises for an information-security course. Students access permitted laboratories and their learning content.

## Product Purpose

Security Lab supports interactive laboratory exercises. PRG-04 maps each laboratory to one or more competencies without recording student mastery or changing evaluation results.

## Operating Context

The competency page opens from a laboratory. An authorized instructor selects and removes existing competencies; other users with laboratory access view the linked competencies. The server checks the session, laboratory access and permission for each change.

## Capabilities and Constraints

Use the existing React/TypeScript frontend and PRG-04 HTTP API. Persist links in PostgreSQL. Handle unavailable services, denied access and interrupted requests without showing another laboratory's data. Competency catalog editing and progress analytics are outside this feature.

## Brand Commitments

Preserve the existing light interface, system typography, white surfaces and blue actions. Keep the Serbian interface text. The user confirmed a dedicated page opened from a laboratory.

