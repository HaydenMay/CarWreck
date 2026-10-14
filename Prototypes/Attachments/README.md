# Vehicle attachment investigation

**Status: architecture proposal awaiting design review.** This folder currently contains
inspection evidence and a proposed Blender proof of concept. It does not yet contain
accessory models, deformation demonstrations or new technical renders.

- [Architecture proposal](architecture-proposal.md): recommended mounting contract,
  trade-offs, acceptance cases, runtime handoff and future vehicle requirements.
- [Concrete metadata example](metadata-example.json): proposed record shapes and a hatchback scoop fit definition; explicitly not validated runtime data.
- [Project context](project-context.md): scoped Unity/asset onboarding.
- [Inspection evidence](evidence/inspection.json): fresh read-only Blender source and
  existing-FBX checks, distinguished from the historical Unity import report.

Baseline: main `2a9ca8bebb296557ed75513c9ad892c8f5153d92`.
Branch: `feature/attachment-prototype`. Production vehicle assets and runtime code
are outside the prototype's edit scope. Nothing is merged to main.
