# Why More Detailed LLM Prompts Can Produce Worse Answers

## TL;DR

**Your observation has research behind it: adding more instructions can make an LLM’s answer worse—even when the extra instructions are reasonable.** A 2026 study found that adding requirements already satisfied by a model’s earlier successful answer could still reduce its ability to solve the task. [Qi et al., 2026](https://arxiv.org/html/2601.22047v1)

The useful distinction is **what the extra detail contributes**:

- **Clear goals, relevant facts, essential constraints, and representative examples help communicate what you need.**
- **Long rule lists, interacting requirements, irrelevant context, and unnecessary prescriptions for how to reason can introduce failure modes.**
- **There is no universal ideal prompt length or safe number of instructions.** Results depend on the task, model, constraint types, and their arrangement.
- **The best default is a minimal but sufficient brief:** explain the outcome, provide the necessary information, identify the requirements that matter most, and add instructions in response to observed failures.
- **When a chat deteriorates, consolidate the current requirements and try a fresh chat.** Repeatedly adding corrections to a confused conversation can preserve earlier mistakes. [Anthropic’s guidance](https://www.anthropic.com/research/effective-context-engineering-for-ai-agents), [Laban et al., 2025](https://arxiv.org/html/2505.06120v1)

My practical recommendation: **be explicit about the result you want; add control over the process only when it demonstrably improves that result.**

## Details

### 1. Extra requirements can hurt actual correctness

The paper most directly relevant to your experience is *On the Paradoxical Interference between Instruction-Following and Task Solving*, published as a preprint in January 2026.

Its experimental design is useful:

1. Find a task the model successfully solves.
2. Extract properties of that successful answer—its method, length, style, keywords, or structure.
3. Turn those properties into explicit requirements.
4. Ask the model to solve the task again with those requirements.

The requirements were derived from an answer that already worked, so they were designed to remain compatible with a successful solution. Nevertheless, performance fell across mathematics, multi-hop question answering, and code generation.

The authors describe the finding as:

> “adding even self-evident constraints significantly degrades general task-solving performance.”

For example, Claude Sonnet 4.5’s reported SustainScore on multi-hop QA was **85%**: it successfully answered 85% of the selected previously solved cases after constraints were added. That is a result for this experimental setup, rather than a general accuracy rating for the model. [Qi et al., 2026, §§2–3](https://arxiv.org/html/2601.22047v1)

**What this means for prompting:** an instruction can sound useful, be logically compatible with the answer, and still reduce performance. Adding rules deserves the same scrutiny as any other change.

### 2. Following your rules and doing your task are different abilities

A model can satisfy the requested tone, headings, and sentence lengths while producing a weak argument or incorrect solution.

The MOSAIC benchmark makes this distinction explicit. It studies text generation with up to 20 constraints and examines their type, quantity, ordering, and interactions. Its central finding is that:

> “compliance is not a monolithic capability”

Some constraints reinforce each other; others create tradeoffs. For example, requiring particular technical keywords can interfere with a target for easy readability. Positional effects also differed across models. [Purpura et al., 2026, §§3–4](https://arxiv.org/html/2601.18554v1)

This helps explain why a highly specified response can feel terrible: it may satisfy many locally measurable requirements while missing the quality you care about most.

There is also a simple reliability issue. **As an illustration, not a measurement of LLMs**, if each independent requirement had a 95% chance of being satisfied, satisfying all 20 would have a probability of about 36%. Real requirements are not independent, but the example shows why “usually follows each rule” does not imply “usually follows the whole specification.”

A useful practical step is to distinguish:

- **Essential:** a violation makes the answer unusable.
- **Preferred:** desirable when compatible with the main task.
- **Optional:** polish that you can request afterward.

That prioritization is my recommendation based on the evidence; it is not a proven universal prompting formula.

### 3. Instruction overload is measurable—but there is no magic limit

IFScale tested 20 models from seven providers on a business report task with increasing numbers of keyword-inclusion instructions, up to 500. At the maximum density, the best tested models achieved about **68% instruction accuracy**. The study also found different degradation patterns and a tendency to favor earlier instructions. [Jaroslawicz et al., 2025](https://arxiv.org/abs/2507.11538)

The qualification matters: **500 keyword requirements are an extreme, artificial workload.** The result does not establish that models fail after five rules, or that a long research brief is inherently bad.

Together, IFScale and MOSAIC support a narrower conclusion: instruction following becomes less reliable as requirements accumulate, and the way requirements interact matters alongside their count. [IFScale](https://arxiv.org/abs/2507.11538), [MOSAIC](https://arxiv.org/html/2601.18554v1)

### 4. Length, complexity, and explicitness should be judged separately

These are different properties of a prompt:

| Property | Example | What to assess |
|---|---|---|
| Useful specificity | “Compare these options for a beginner with a €500 budget.” | Does it resolve an important ambiguity? |
| Necessary context | The source text, relevant code, or facts needed to answer | Would removing it make the task harder or impossible? |
| Additional constraints | Exact counts, required phrases, tone rules, exceptions | Does each requirement justify its cost and interactions? |
| Process prescription | A mandatory sequence of analysis and revision steps | Has that sequence actually improved this task? |
| Irrelevant material | Old drafts, obsolete decisions, unrelated background | Can it distract from the current task? |

The evidence for distraction goes back at least to an ICML 2023 study that inserted irrelevant information into arithmetic problems and observed substantial performance degradation. Some extra instructions, including telling the model to ignore irrelevant information, helped mitigate the problem—another reason “all additional instructions are bad” would be the wrong conclusion. [Shi et al., 2023](https://arxiv.org/abs/2302.00093)

Chroma’s 2025 technical report extended the investigation across 18 models, including GPT, Claude, Gemini, and Qwen families. It found increasingly unreliable performance as input length grew in controlled retrieval and replication tasks. In a conversational-memory experiment, focused inputs containing the relevant material outperformed the full histories. This is original experimental work, although it is an industry technical report rather than a peer-reviewed paper. [Hong et al., 2025](https://www.trychroma.com/research/context-rot)

**My synthesis:** keep information that helps solve the problem. Remove material that merely makes the prompt look more comprehensive.

### 5. Where you put information can matter

The peer-reviewed *Lost in the Middle* study found that performance was often strongest when relevant information appeared at the beginning or end of a long input, with worse performance when it appeared in the middle. Its tasks involved document question answering and key-value retrieval. [Liu et al., TACL 2024](https://aclanthology.org/2024.tacl-1.9/)

That is evidence about particular models and tasks, not a law that every model always ignores the middle. Chroma, for example, found no notable position effect in one of its specific retrieval experiments, while MOSAIC found differing positional patterns across models. [Chroma report](https://www.trychroma.com/research/context-rot), [MOSAIC](https://arxiv.org/html/2601.18554v1)

A reasonable starting arrangement is:

- State the goal and essential requirements clearly.
- Separate reference material from instructions.
- After a large document block, restate the specific question to answer.

Google’s Gemini guidance similarly recommends putting essential behavioral instructions near the beginning and placing the specific query after large amounts of context. [Google’s prompt design guide](https://aistudio.google.com/docs/prompting-strategies)

### 6. Detailed instructions for “how to think” are model dependent

Older prompting advice often presents “think step by step” as a default improvement. Research now supports a more conditional view.

A 2025 report found that explicit chain-of-thought prompting could produce modest average gains for some non-reasoning models, while increasing variability and occasionally introducing errors. For reasoning models, accuracy gains were often marginal or absent, with higher token use and response time. [Meincke et al., 2025](https://arxiv.org/abs/2506.07142)

OpenAI’s reasoning-model guidance says:

> “Keep prompts simple and direct”

It also warns that step-by-step prompting can sometimes hinder performance. This guidance is specifically about its reasoning models, so it should not be treated as a rule for every LLM. [OpenAI’s reasoning best practices](https://developers.openai.com/api/docs/guides/reasoning-best-practices)

Google likewise says its thinking models generally do not need to outline their reasoning in the returned response, while retaining more elaborate planning guidance for complex agent workflows. [Google’s guide](https://aistudio.google.com/docs/prompting-strategies)

**Practical implication:** specify the evidence and checks you need—such as showing calculations, testing code, or explaining assumptions. Treat a mandatory reasoning script as something to evaluate on your task.

### 7. Small wording and formatting changes can have unpredictable effects

An ICLR 2024 paper studied meaning-preserving changes to prompt formatting. It found large performance differences in several open models, including extreme differences for LLaMA-2-13B in few-shot settings. It also found that a format that worked well for one model did not reliably transfer to another. [Sclar et al., ICLR 2024](https://proceedings.iclr.cc/paper_files/paper/2024/file/6c0e99d736da621403018ca7b32b1a4d-Paper-Conference.pdf)

Those older-model results should not be used to predict the same magnitude of sensitivity in current frontier models. They do establish why a single impressive answer is weak evidence that a prompt technique works reliably.

This is also why elaborate templates copied from the internet deserve testing. A template may encode a useful task structure, but its extra wording is not evidence of effectiveness.

### 8. “Break it into steps” needs an important qualification

There are two different workflows:

- **Decompose the work:** research → synthesize → edit, with a clear brief and a defined output for each stage.
- **Reveal requirements gradually:** ask an underspecified question, receive an answer, then repeatedly add requirements and corrections.

Google recommends chaining prompts for suitable sequential tasks. [Google’s guide](https://aistudio.google.com/docs/prompting-strategies)

But research on more than 200,000 simulated conversations found that gradually revealing requirements could produce substantially worse outcomes than supplying the complete task initially. Models made early assumptions and relied too heavily on earlier answer attempts. The study focused on particular generation tasks and simulated conversations, so it does not establish that ordinary follow-up questions are generally harmful. [Laban et al., 2025](https://arxiv.org/html/2505.06120v1)

Its practical advice includes:

> “Consolidate before retrying.”

You can use conversation to discover what you want, then assemble the current requirements into a coherent brief for execution. That preserves the value of exploration while reducing the burden of reconstructing your intent from an increasingly tangled history. [Laban et al., §7.4](https://arxiv.org/html/2505.06120v1#S7.SS4)

### 9. What I would change in your prompting practice

Start with these four components:

```text
Task: [What you want produced or solved.]

Context: [Facts, references, and audience information needed to do it.]

Essential requirements:
- [Requirement that materially affects usefulness.]
- [Another essential requirement.]

Output: [The structure and approximate depth you want.]
```

This is a starting point, not a requirement to use labels or fill every field.

Then make changes according to the failure:

| Observed failure | Useful next change |
|---|---|
| Misunderstands the goal | Clarify the purpose, audience, or decision the answer must support |
| Lacks information | Supply the missing facts or sources |
| Wrong style or format | Provide a representative example |
| Misses an essential requirement | Make that requirement clearer and more prominent |
| Becomes stiff or checklist-like | Remove secondary style rules and reassess the main output |
| Keeps repeating an earlier mistake | Consolidate the brief and retry in a fresh chat |
| Gets facts or calculations wrong | Require appropriate evidence, tools, or external verification |

The provider guidance broadly supports this iterative approach. Anthropic recommends beginning with a minimal prompt and adding instructions and examples based on observed failures. It explicitly cautions:

> “minimal does not necessarily mean short”

It also discourages stuffing prompts with a laundry list of edge cases. [Anthropic’s context engineering guidance](https://www.anthropic.com/research/effective-context-engineering-for-ai-agents)

Examples are useful, but they are also conditional: Google strongly recommends them, while OpenAI advises trying reasoning models without examples first and adding examples when needed. That disagreement is a good reason to test against the model and task you actually use. [Google](https://aistudio.google.com/docs/prompting-strategies), [OpenAI](https://developers.openai.com/api/docs/guides/reasoning-best-practices)

### 10. How to test whether your longer prompts are hurting

For a small practical experiment, take several tasks representative of your actual use and compare:

- **A:** your concise task description.
- **B:** the same description plus necessary context and essential requirements.
- **C:** your full elaborate prompt.

Use fresh chats, the same model and settings, and the same source material. Repeat each version a few times. Where possible, evaluate answers without knowing which prompt produced them.

Score two things separately:

1. **Task quality:** correctness, usefulness, depth, judgment, or writing quality.
2. **Requirement satisfaction:** whether the answer followed the requested constraints.

If B consistently beats A, the extra specificity is earning its place. If C follows more rules but produces worse work, you have identified a tradeoff that matters in your workflow.

The research supports your experience as a real failure mode. It cannot determine which parts of your own prompts cause it without comparing those prompts and outputs.

## Sources

Research reviewed using Chrome. The sources span different model generations; benchmark results should be read within those scopes.

1. **[Qi et al. — *On the Paradoxical Interference between Instruction-Following and Task Solving*](https://arxiv.org/html/2601.22047v1)** — January 2026 preprint. The most direct evidence that adding compatible requirements can reduce task correctness.

2. **[Purpura et al. — *Deconstructing Instruction-Following: A New Benchmark for Granular Evaluation of Large Language Model Instruction Compliance Abilities*](https://arxiv.org/html/2601.18554v1)** — January 2026 preprint introducing MOSAIC. Examines constraint types, interactions, counts, and ordering.

3. **[Jaroslawicz et al. — *How Many Instructions Can LLMs Follow at Once?*](https://arxiv.org/abs/2507.11538)** — July 2025 preprint introducing IFScale. Tests instruction density using up to 500 keyword requirements.

4. **[Liu et al. — *Lost in the Middle: How Language Models Use Long Contexts*](https://aclanthology.org/2024.tacl-1.9/)** — TACL 2024, peer reviewed. Evidence about retrieving relevant information at different positions in long inputs.

5. **[Hong, Troynikov, and Huber — *Context Rot: How Increasing Input Tokens Impacts LLM Performance*](https://www.trychroma.com/research/context-rot)** — July 2025 Chroma technical report. Controlled experiments across 18 models; includes methods, limitations, and replication code.

6. **[Shi et al. — *Large Language Models Can Be Easily Distracted by Irrelevant Context*](https://arxiv.org/abs/2302.00093)** — ICML 2023, peer reviewed. Tests irrelevant information in arithmetic reasoning problems and possible mitigations.

7. **[Laban et al. — *LLMs Get Lost In Multi-Turn Conversation*](https://arxiv.org/html/2505.06120v1)** — May 2025 study. Investigates gradually revealed requirements, premature assumptions, and consolidation strategies.

8. **[Sclar et al. — *Quantifying Language Models’ Sensitivity to Spurious Features in Prompt Design*](https://proceedings.iclr.cc/paper_files/paper/2024/file/6c0e99d736da621403018ca7b32b1a4d-Paper-Conference.pdf)** — ICLR 2024, peer reviewed. Studies sensitivity to meaning-preserving formatting changes.

9. **[Meincke et al. — *Prompting Science Report 2: The Decreasing Value of Chain of Thought in Prompting*](https://arxiv.org/abs/2506.07142)** — June 2025 research report/preprint. Compares the benefits and costs of explicit chain-of-thought prompting.

10. **[Anthropic — *Effective Context Engineering for AI Agents*](https://www.anthropic.com/research/effective-context-engineering-for-ai-agents)** — Official engineering guidance. Recommends sufficient, focused context and iterative additions based on failures.

11. **[OpenAI — *Reasoning Best Practices*](https://developers.openai.com/api/docs/guides/reasoning-best-practices)** — Official model-specific guidance. Covers straightforward instructions, examples, delimiters, and reasoning prompts.

12. **[Google — *Prompt Design Strategies*](https://aistudio.google.com/docs/prompting-strategies)** — Official Gemini guidance. Covers specificity, examples, prompt decomposition, ordering, and thinking models.