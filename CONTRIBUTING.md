# Contributing

Contributions are welcome while the project is experimental.

## Before coding

Read:

- `AGENTS.md`
- `docs/ARCHITECTURE.md`
- `docs/PORTING.md`
- `docs/V1-SCOPE.md`

## Principles

- Keep commits narrow and reversible.
- Do not claim a feature is supported until it has a reproducible manual or automated verification path.
- Treat `.comp` packages as untrusted input.
- Preserve upstream attribution for derived code.
- Avoid copying Apple-specific architecture when Windows has a better-native equivalent.

## Pull requests

A useful PR should explain:

- the user-visible behavior;
- whether behavior is derived from upstream Compositor;
- which `.comp` fields are affected;
- how the change was verified;
- known compatibility gaps.
