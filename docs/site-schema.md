# Site definition schema

A site is stored as two JSON snapshots on the `Sites` row, both in `jsonb` columns:

| Column      | Meaning                                                        |
| ----------- | -------------------------------------------------------------- |
| `Draft`     | What the owner is editing. Never served to visitors.             |
| `Published` | What visitors see. `NULL` until the site is published once.      |

Editing writes only to `Draft`. `Site.Publish()` deep-copies `Draft` over `Published`, so the
live site never shares objects with the draft that is still being edited. `Site.DiscardDraft()`
copies in the other direction.

Both snapshots use the same shape (`SiteDefinition`), so the renderer does not care which one it
was handed — that is what lets the editor preview a draft through the real renderer.

## Shape

```jsonc
{
  "schemaVersion": 1,
  "meta":    { "businessName": "...", "tagline": "...", "seoTitle": "...", "seoDescription": "..." },
  "theme":   { "palette": { "primary": "#1f5eff", ... }, "fonts": { "heading": "Inter", "body": "Inter" } },
  "sections": [ { "type": "hero", "id": "...", "visible": true, "headline": "..." } ]
}
```

`theme` is deliberately separate from section content: restyling a site must never require
touching a word of copy, and the AI generator writes the two independently.

Sections are an ordered list — order in the array is order on the page. Each carries a `type`
discriminator (`hero`, `about`, `services`, `gallery`, `testimonials`, `contact`, `hoursMap`,
`cta`). **Those strings are persisted data**: renaming one is a schema migration, not a rename.

## Versioning and migration

`schemaVersion` is written on every save. `SiteDefinitionSerializer` is the only place definitions
cross the JSON boundary, and on read it upgrades an older document to the current version before
anything else sees it. The rest of the app therefore only ever handles the current shape.

To change the schema:

1. Add an upgrade step to `SiteDefinitionSerializer.Upgrades`, keyed by the version it upgrades
   *from*. It rewrites the `JsonObject` in place and must leave a document valid at key + 1.
2. Bump `SiteDefinition.CurrentSchemaVersion`.
3. Add a round-trip test that starts from a real document at the old version.

Rules that keep this safe:

- **Never edit a published upgrade step.** Rows already upgraded will not be revisited, so
  changing a step means two databases disagree about what the same version means. Add a new step.
- **Never renumber versions.** Version numbers are stored in customer data.
- Migration happens on read, not as a bulk `UPDATE`. A row is rewritten in the new version only
  when it is next saved, so old and new documents coexist in the table indefinitely and both
  must stay readable.
- A document whose `schemaVersion` is *newer* than the running build throws rather than loading.
  After a rollback, an old build would otherwise quietly drop fields it does not understand and
  write the truncated result back, destroying data. Failing loudly is the safer outcome.

`schemaVersion: 0` means a document written before the field existed and is treated as the
starting point of the upgrade chain.
