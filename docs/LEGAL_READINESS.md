# StepPilot – Legal & Compliance Readiness

> This document is a product/deployment checklist, not legal advice and not a certification of legal compliance.

## 1. Scope of the built-in compliance support

StepPilot can support employers with configurable checks around planning and recorded working time. The current Germany-oriented baseline includes checks or warnings for daily working time, break duration, rest periods, Sunday/public-holiday work and compensatory rest. Company-specific exceptions and legal bases must be configured and reviewed by the operator.

The application must never market these checks as a guarantee that a schedule or time record is legally compliant. Collective agreements, works agreements, sector-specific rules, youth protection, maternity protection, public-service rules, transport rules and individual exceptions can change the legal result.

## 2. German working-time baseline

For a standard German employment context, the product logic should be validated against the currently applicable ArbZG before each production release. Key baseline rules include:

- § 3 ArbZG: generally 8 hours per working day, extendable to 10 hours only subject to the statutory averaging period.
- § 4 ArbZG: at least 30 minutes break for more than 6 up to 9 hours, and 45 minutes for more than 9 hours; work should not continue for more than 6 hours without a break.
- § 5 ArbZG: generally at least 11 consecutive hours of rest.
- §§ 9–11 ArbZG: Sunday/public-holiday restrictions, exceptions and compensatory-rest requirements.
- § 16 ArbZG: statutory working-time record obligations under the current text of the Act must be observed alongside the broader duties resulting from applicable case law and occupational-safety law.

Before selling StepPilot as a production product, the exact rule set and wording should be reviewed by a German employment-law professional.

## 3. Data protection / GDPR readiness

StepPilot processes personal data such as employee master data, availability, absences, schedules and time records. A production operator must document at minimum:

- controller / processor roles and Art. 28 GDPR processor agreements where applicable;
- purposes and legal bases for each processing activity;
- Art. 13/14 privacy information;
- role and access concepts based on least privilege;
- retention and deletion periods;
- processes for access, correction, deletion, restriction and portability requests where applicable;
- a record of processing activities and, where required, a DPIA;
- technical and organizational measures under Art. 32 GDPR;
- incident response and personal-data-breach procedures;
- hosting locations, sub-processors and international-transfer safeguards.

StepPilot's audit trail should be treated as security/compliance evidence and protected accordingly. Audit data must itself have a justified retention period.

## 4. Time tracking

The time-tracking module should preserve original records and make corrections traceable. Manual corrections require a reason and approval workflows should identify the approving account. Production deployments should define who may edit records, how corrections are communicated to employees, and how long records are retained.

Time records should use a clearly documented timezone policy. Stored UTC timestamps and explicit local conversion are preferred. The operator must ensure that daylight-saving changes and overnight shifts are handled consistently.

## 5. Works council and employee participation

Where a works council exists, introducing or materially changing workforce-management, scheduling or time-tracking software may trigger co-determination rights. The deploying company must evaluate this before rollout.

## 6. Mandatory customer-specific documents before production

The following cannot be generated correctly from source code alone because they depend on the actual operator and deployment:

- legal notice / Impressum;
- privacy notice;
- terms / SaaS contract and service description;
- data-processing agreement (AVV/DPA);
- TOM documentation;
- retention/deletion concept;
- sub-processor list;
- backup / restore and incident-response procedures;
- employment-law or works-agreement documentation where required.

## 7. Release gate

A production release should only be marked ready when all of the following are true:

- CI build passes in Release mode with warnings treated as errors.
- Database migrations have been tested on a copy of production-like data.
- Authorization and tenant-isolation tests pass.
- Backup and restore have been tested.
- Security headers, TLS, secrets and production logging are configured.
- Compliance rules have been reviewed for the customer's country, sector and agreements.
- Privacy/contract documents contain the real operator data and have been legally reviewed.
- A manual end-to-end test of import → employee setup → planning → compliance → publication → time tracking → correction → approval → reporting has passed.

## 8. Product wording

Recommended wording: **“StepPilot unterstützt bei der Prüfung von Arbeitszeit- und Planungsregeln.”**

Avoid wording such as **“garantiert rechtskonform”, “100 % gesetzeskonform”** or similar absolute claims unless an appropriately qualified legal review supports that precise claim for the defined jurisdiction and use case.
