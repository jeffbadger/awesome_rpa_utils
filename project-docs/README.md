# Project Documentation

Reference material and standing standards for this repository, kept outside
`src/` so it can evolve (and, if ever needed, be excluded from Git) without
touching the component source trees.

- [`coding-standards/`](coding-standards/) — repository-wide contracts every
  utility project must follow: the
  [Never-Throws Standard](coding-standards/never-throws-standard.md) (plus
  its [implementation record](coding-standards/never-throws-implementation.md))
  and the [Signature Uniqueness Standard](coding-standards/signature-uniqueness-standard.md).
- [`pega-usability-reviews/`](pega-usability-reviews/) — per-component
  reviews of how usable each public method is from the Pega Robot Studio
  designer surface, plus the suite-wide
  [index and methodology](pega-usability-reviews/README.md).
- [`plans/`](plans/) — implementation plans for substantial, multi-task
  features (e.g. the [REST code generator](plans/2026-08-31-swagger-rest-codegen.md)
  and its [multi-format input support](plans/2026-09-06-multi-format-rest-codegen.md)).
