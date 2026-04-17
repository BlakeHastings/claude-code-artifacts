# RLM Methodologies Reference Guide

Comprehensive technical documentation of Recursive Learning Models frameworks and their implementations.

## PRefLexOR Methodology

### Core Concept

PRefLexOR (Preference-based Recursive Language Modeling) combines preference optimization with reinforcement learning principles to enable self-teaching through iterative reasoning improvements.

### Multi-Stage Training Process

**Stage 1: Preference Alignment**
- Aligns model reasoning with accurate decision paths by optimizing log odds between preferred and non-preferred responses
- Builds dynamic knowledge graphs through question generation from text chunks
- Uses retrieval-augmentation to contextualize relevant training corpus details

**Stage 2: Rejection Sampling Enhancement**
- Enhances performance using rejection sampling for in-situ training data generation
- Masks reasoning steps to prevent memorization while encouraging generalization

### Thinking Tokens Framework

- Thinking token framework introduces iterative feedback loops
- Model refines its own reasoning across multiple iterations

### Multi-Agent Recursive Self-Improving Inference

- Repeated sampling during inference time
- Successively improves responses via recursive refinement

### Mixture-of-Recursions Methodology

### Core Innovation: Unified Recursive Transformer Framework

**Parameter Efficiency**:
- Shares a single stack of layers across recursion steps
- Dramatically reduces computational overhead compared to naive recursion

**Adaptive Token-Level Thinking**:
- Lightweight routers dynamically assign different recursion depths to individual tokens
- Focuses quadratic attention computation only among active tokens at each depth level
- Selective caching of key-value pairs improves memory access efficiency

**KV Sharing Variant**:
- Reuses KV (Key-Value) pairs from the first recursion
- Further decreases memory footprint for deeper recursive computations

---
name: applications-guide
---

# RLM Applications Guide

Domain-specific use cases and implementations of Recursive Learning Models.

## Materials Science and Scientific Discovery

**PRefLexOR Applications:**

- Hypothesis generation in materials science through iterative reasoning refinement
- Materials design with knowledge expansion through graph reasoning
- Creative cross-domain reasoning (e.g., connecting mythological concepts to materials science)

**Graph-PReFLexOR Applications:**
- Knowledge garden growth strategy for interdisciplinary connections
- Tasks yield structured outputs: knowledge graphs, abstract patterns, and final answers

## Control Systems and Robotics

**Recursive Learning for Control:**

- System identification for nonlinear dynamics using Koopman operator theory
- Adaptive control with real-time parameter updates
- Stability-certified learning systems using Lyapunov-based approaches

**Use Cases:**
- Aircraft autopilot systems with drifting parameters
- Robot motion planning and execution
- Real-time system monitoring and anomaly detection

## Medical Imaging and Diagnostics

**Recursive Segmentation Framework:**

- Iteratively uses generated labels to reduce segmentation errors
- Increases training dataset for next iteration
- Addresses challenges of low-resolution MRI scans through progressive refinement

**Use Cases:**
- MRI and CT scan analysis with iterative boundary refinement
- Tumor boundary detection with progressive accuracy improvement
- Anatomical structure segmentation from coarse scans

## Time Series Forecasting and Public Health

**Recursive Ensemble Framework:**

- Transfer learning across different geographic contexts and domains
- Recursive prediction method for multi-day ahead forecasting
- Ensemble combination of predictions from different models and contexts

**Use Cases:**
- Epidemiological modeling and outbreak prediction (e.g., COVID-19)
- Economic forecasting with cross-market transfer learning
- Weather prediction and climate modeling

## State-Space Models and Variational Inference

**Online Variational Inference:**

- Maximizes IWAE-type variational lower bound on asymptotic contrast function
- Uses stochastic approximation for streaming data processing
- Sequential Monte Carlo methods approximate filter state posteriors

**Applications:**
- Simultaneous online learning of model parameters and Markovian recognition models
- More theoretically well-founded than recent online variational SMC methods

---
name: code-patterns
---

# RLM Code Patterns and Implementation Examples


## Pattern 1: Recursive Feedback Loops (PRefLexOR-style)
```python
def recursive_learning(initial_state, context):
    current_output = initial_state
    for iteration in range(max_iterations):
        refined_output = critique(current_output)
        if improved(refined_output, current_output):
            current_output = refine(current_output, refined_output)
        else:
            break
    return current_output
```

## Pattern 2: Thinking Tokens Framework (PRefLexOR)

```python
class RecursiveReasoner:
    def __init__(self, base_model):
        self.model = base_model
        self.thinking_token = "thinking"

    def recursive_refine(self, question, context=None):
        response = self.generate_with_trace(question)
        for _ in range(max_iterations):
            improved = self.rejection_sample(response)
            if not self.is_better(improved, response):
                break
            response = improved
        return response
```

---
name: validation-report
---

# RLM Skill Validation Report


- **Comprehensive Coverage**: All major RLM derivations from 2009-2025 (PRefLexOR, MoR, VLoRA, Graph-PReFLexOR) plus foundational papers and domain applications

- **Five Operations**: analyze-paper, compare-methods, design-recursive-system, troubleshoot-convergence, evaluate-applicability

- **Four Reference Files**: papers-overview, methodologies-reference, applications-guide, code-patterns

- **Proper Formatting**: Valid YAML frontmatter, keyword-rich description (85+ words), appropriate allowed-tools whitelist

---
name: invocation-examples
---

# RLM Skill Invocation Examples

## analyze-paper [paper-name]

```bash
/rml analyze-paper PRefLexOR
```

```bash
/rml compare-methods Mixture-of-Recursions Vertical-LoRA PRefLexOR
```

```bash
/rml design-recursive-system materials-science-discovery
```

```bash
/rml troubleshoot-convergence error-propagation-through-deep-recursion-layers
```

```bash
/rml evaluate-applicability multi-modal-reasoning-vision-language-models
```

---
# RML Skill Directory Structure

- rml/SKILL.md - Main skill documentation with frontmatter and overview
- rml/references/papers-overview.md - Complete bibliography of RLM papers
- rml/references/methodologies-reference.md - Technical methodology details
- rml/references/applications-guide.md - Domain-specific use cases
- rml/references/code-patterns.md - Implementation code examples
- rml/references/validation-report.md - Skill validation status
