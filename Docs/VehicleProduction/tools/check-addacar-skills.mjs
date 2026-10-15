// Consistency check for the two addacar entry points (Claude Code + Codex): same description and body, explicit-request
// policy (plain-language requests allowed, nothing else), referenced docs present, workflow checkpoints present.
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
if (parsed.claude && parsed.codex && parsed.claude.front.description !== parsed.codex.front.description) errors.push("descriptions differ between Claude Code and Codex");
for (const [agent, p] of Object.entries(parsed)) {
  const d = p.front.description ?? "";
  if (!/ONLY when the user explicitly asks/.test(d)) errors.push(agent + ": description must limit the skill to explicit requests");
  if (!/vehicle creation/.test(d)) errors.push(agent + ": description must name the CarWreck vehicle creation workflow (plain-language trigger)");
  if (p.front["disable-model-invocation"] === "true") errors.push(agent + ": disable-model-invocation blocks plain-language requests");
}
const yaml = ".agents/skills/addacar/agents/openai.yaml";
if (!existsSync(yaml) || !/allow_implicit_invocation:\s*true/.test(readFileSync(yaml, "utf8"))) errors.push(`codex: ${yaml} should set policy.allow_implicit_invocation: true (plain-language requests)`);
const wfPath = "Docs/VehicleProduction/addacar-workflow.md";
const wf = existsSync(wfPath) ? readFileSync(wfPath, "utf8") : "";
for (const cp of ["CHECKPOINT A", "CHECKPOINT B", "CHECKPOINT C", "Reference self-review"]) if (!wf.includes(cp)) errors.push(`workflow is missing ${cp}`);
for (const doc of ["Docs/VehicleProduction/addacar-workflow.md", "Docs/VehicleProduction/vehicle-asset-spec.md", "Docs/VehicleProduction/vehicle-qa-checklist.md"])
  if (!existsSync(doc)) errors.push(`${doc} missing`);
console.log(errors.length ? "ADDACAR_SKILLS FAIL\n- " + errors.join("\n- ") : "ADDACAR_SKILLS PASS (both entry points consistent, all referenced docs present)");
process.exit(errors.length ? 1 : 0);
