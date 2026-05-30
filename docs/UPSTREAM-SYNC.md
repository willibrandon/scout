# Upstream Sync Policy

Scout targets one ripgrep revision at a time. The current pin is:

```text
4857d6fa67db69a95cd4b6f2adda5d807d4d0119
```

To advance the pin:

1. Update the reference checkout at `/Users/brandon/src/ripgrep`.
2. Verify `git -C /Users/brandon/src/ripgrep rev-parse HEAD` is the intended
   new commit.
3. Replace `upstream/Cargo.lock` with the reference checkout's `Cargo.lock`.
4. Regenerate vendored Unicode data and update `upstream/UNICODE-VERSION`.
5. Update dependency `UPSTREAM.md` files for every affected ported dependency.
6. Rebuild the Native AOT entry spike for all six RIDs.
7. Run the full unit, integration, differential, encoding, regex, and
   performance gates.
8. Record any intentional byte-level deviation in `docs/PARITY.md` with a
   guarding test.

The pin never advances as part of a drive-by dependency update.
