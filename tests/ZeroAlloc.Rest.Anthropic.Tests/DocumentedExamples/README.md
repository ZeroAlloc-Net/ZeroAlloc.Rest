# Documented examples

JSON copied verbatim from Anthropic's documentation (fetched 2026-10-25). Nothing here is invented.
Each file is one JSON payload; JSON has no comments, so the sources are listed here.

- `events/*.json`: the `data:` payload of one server-sent event, from
  https://docs.claude.com/en/docs/build-with-claude/streaming
  (now https://platform.claude.com/docs/en/build-with-claude/streaming): the basic streaming
  request, tool use streaming, extended thinking streaming, web search streaming and "Error events".
  Files ending in `-UNKNOWN` carry a block, delta or field this package deliberately does not model.
- `responses/basic.json`, `responses/max_tokens.json`: the "Output" of "Basic request and response"
  and "Prefilling Claude's response" in
  https://platform.claude.com/docs/en/build-with-claude/working-with-messages
  (the Messages API guide, linked from https://docs.claude.com/en/api/messages).
- `responses/text-and-tool_use-partial.json`: the "Model responses with tools" example in
  https://platform.claude.com/docs/en/agents-and-tools/tool-use/define-tools. The docs show it
  without `id`, `model` and `usage`; it is kept as published.

The reference page https://docs.claude.com/en/api/messages itself publishes no response body.
