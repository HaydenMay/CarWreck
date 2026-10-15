// Consistency check for the two addacar entry points (Claude Code + Codex).
// Run from the repository root:  node Docs/VehicleProduction/tools/check-addacar-skills.mjs
import { readFileSync, existsSync } from "node:fs";

const files = { claude: ".claude/skills/addacar/SKILL.md", codex: ".agents/skills/addacar/SKILL.md" };
const errors = [];
const parsed = {};
for (const [agent, path] of Object.entries(files)) {
  if (!existsSync(path)) { errors.push(`${agent}: ${path} missing`); continue; }
  const text = readFileSync(path, "utf8").replace(/\r\n/g, "\n");
  const m = text.match(/^---\n([\s\S]*?)\n---\n([\s\S]*)$/);
  if (!m) { errors.push(`${agent}: no YAML frontmatter`); continue; }
  const front = Object.fromEntries(m[1].split("\n").filter(l => l.includes(":")).map(l => [l.slice(0, l.indexOf(":")).trim(), l.slice(l.indexOf(":") + 1).trim()]));
  parsed[agent] = { front, body: m[2] };
  if (front.name !== "addacar") errors.push(`${agent}: name must be addacar (is ${front.name})`);
  if (!front.description) errors.push(`${agent}: description missing`);
  for (const doc of m[2].match(/`(Docs\/[^`]+|Blender\/Scripts\/[^`]+\.py)`/g) ?? []) {
    const p = doc.slice(1, -1);
    if (!p.includes("*") && !existsSync(p)) errors.push(`${agent}: referenced ${p} does not exist`);
  }
}
if (parsed.claude && parsed.codex && parsed.claude.body !== parsed.codex.body) errors.push("SKILL.md bodies differ between Claude Code and Codex");
if (parsed.claude && parsed.claude.front["disable-model-invocation"] !== "true") errors.push("claude: disable-model-invocation should be true (explicit /addacar only)");
const yaml = ".agents/skills/addacar/agents/openai.yaml";
if (!existsSync(yaml) || !/allow_implicit_invocation:\s*false/.test(readFileSync(yaml, "utf8"))) errors.push(`codex: ${yaml} should set policy.allow_implicit_invocation: false`);
for (const doc of ["Docs/VehicleProduction/addacar-workflow.md", "Docs/VehicleProduction/vehicle-asset-spec.md", "Docs/VehicleProduction/vehicle-qa-checklist.md"])
  if (!existsSync(doc)) errors.push(`${doc} missing`);
console.log(errors.length ? "ADDACAR_SKILLS FAIL\n- " + errors.join("\n- ") : "ADDACAR_SKILLS PASS (both entry points consistent, all referenced docs present)");
process.exit(errors.length ? 1 : 0);
