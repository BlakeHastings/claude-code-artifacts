---
name: rml
description: >-
  Recursive Learning Models (RLM) for deep exploration of context space. Analyzes PRefLexOR, Mixture-of-Recursions, Vertical LoRA, Graph-PReFLexOR, and related frameworks to enable iterative reasoning refinement, multi-agent self-correction, preference-based optimization with thinking tokens, adaptive token-level computation, graph-based symbolic reasoning, and recursive state updates for exploratory optimization in materials science, scientific discovery, complex problem solving, agentic AI thinking, knowledge expansion through dynamic graph construction, transfer learning across domains, stability-certified control systems, time series forecasting refinement, medical image segmentation improvement, and multi-step ahead prediction with iterative feedback loops.
argument-hint: "[operation] [target]"
allowed-tools: Read, Glob, Grep, WebFetch, Bash, Task
---
# Recursive Learning Models (RLM)

## Overview

This skill enables deep exploration of **Recursive Learning Models** — a paradigm shift in machine learning that empowers systems to iteratively refine their outputs through multi-step reasoning, feedback loops, self-correction mechanisms, and preference-based optimization. RLM represents the evolution from static feed-forward approaches to dynamic, iterative learning systems capable of deeper cognitive processing.

## Core Capabilities

### 1. PRefLexOR Analysis
- **Preference-Based Recursive Language Modeling** for exploratory optimization of reasoning and agentic thinking
- Thinking tokens framework with iterative feedback loops
- Multi-stage training: preference alignment + rejection sampling refinement
- Dynamic knowledge graph construction through question generation from text chunks
- Rejection sampling for in-situ training data generation while masking reasoning steps

### 2. Mixture-of-Recursions (MoR) Analysis
- **Learning Dynamic Recursive Depths** for adaptive token-level computation
- Unified recursive transformer framework with shared layers across recursion steps
- Lightweight routers assigning different recursion depths to individual tokens
- Selective caching of key-value pairs for memory efficiency
- KV sharing variants reusing cached representations from first recursion

### 3. Vertical LoRA Analysis
- **Dense Expectation-Maximization Interpretation** of transformers as recursive Bayesian networks
- Each layer recursively learning increments based on prior layer computations
- Orthogonal low-rank adaptation combining with existing LoRA methods
- Dramatic parameter reduction while preserving model performance

### 4. Graph-PReFLexOR Analysis
- **In-situ graph reasoning and knowledge expansion** using symbolic abstraction
- Category theory-inspired concept encoding as nodes with relationship edges
- Hierarchical inference through isomorphic representations
- Knowledge garden growth strategy for interdisciplinary connections
- Hypothesis generation, materials design, creative cross-domain reasoning

## Research Foundations

### Original Papers
1. **RET-LLM** (arXiv:2305.14322) - Initial write-read memory framework with triplet-based knowledge extraction and Davidsonian semantics inspiration
2. **MemLLM** - Evolved, thoroughly evaluated successor to RET-LLM

### Key Derivations Timeline

| Year | Paper | arXiv ID | Primary Contribution |
|------|-------|----------|---------------------|
| 2009 | Stochastic Recursive Learning (Finance) | 0910.1166 | Convergence guarantees for stochastic recursive procedures |
| 2010 | Optimized RNN Algorithm | 1004.1997 | Matrix operations for online learning without manual rate selection |
| 2021 | Transfer-Recursive Ensemble (COVID) | 2108.09131 | Multi-day forecasting with recursive refinement |
| 2022 | Recursive 3D Segmentation | 2203.07846 | Iterative label improvement for medical imaging |
| 2023 | Koopman Operator Learning | 2309.04074 | Data-driven nonlinear system representation |
| 2024 | Stability-Certified LQR | 2403.05367 | Lyapunov-based stability guarantees for recursive learning |
| 2024 | Vertical LoRA | 2406.09315 | EM interpretation of transformers as recursive BNs |
| 2024 | PRefLexOR | 2410.12375 | Preference-based recursive language modeling |
| 2025 | Graph-PReFLexOR | 2501.08120 | Symbolic abstraction with graph reasoning |
| 2025 | Mixture-of-Recursions | 2507.10524 | Adaptive token-level computation depths |

### Control Systems Applications
- Tiwari et al. (arXiv:2309.04074): Koopman operator theory with deep learning recursive representation for system identification
- Sforni et al. (arXiv:2403.05367): Stability-certified LQR via recursive least squares + policy gradient with Lyapunov guarantees

### Domain Applications Summary

| Domain | Application | Key Innovation |
|--------|-------------|----------------|
| Medical Imaging | 3D Shoulder Joint Segmentation | Iterative label refinement reducing segmentation errors |
| Time Series | COVID-19 Multi-Day Forecasting | Transfer learning + recursive ensemble predictions |
| Control Systems | Aircraft LQR with Drifting Parameters | Stability-certified on-policy learning |
| State-Space Models | Online Variational Inference | Sequential Monte Carlo for streaming data |

## Analysis Frameworks

### Iterative Refinement Loop Pattern
```
Initial Output → Critique/Feedback → Adjustment → Re-evaluation → Final Output
```

### Recursive State Updates
- Each iteration builds upon previous computations
- Contextual information maintained through recursive state variables
- Progressive improvement rather than single-pass processing

### Feedback Loop Architectures
1. **Closed-loop refinement**: Continuous feedback within single inference pass
2. **Multi-round sampling**: Multiple independent generation attempts with selection
3. **Cross-model ensemble**: Combining predictions from different model instances

## Methodological Components

| Component | Description | Use Case |
|-----------|-------------|----------|
| Thinking Tokens | Explicit modeling of intermediate computational states | Reflective processing, self-correction |
| Rejection Sampling | Quality control mechanism for iterative refinement | Ensuring output quality across iterations |
| Preference Optimization | RL-style optimization with preferred/non-preferred response comparison | Aligning reasoning paths with accurate solutions |
| Dynamic Depth Selection | Router-based assignment of different recursion depths per token | Computational efficiency optimization |
| Graph Reasoning Integration | Symbolic abstraction supporting hierarchical inference | Cross-domain knowledge connection and expansion |

## Available Operations

### `analyze-paper [paper-name]`
Analyze a specific RLM paper or derivation in depth, extracting methodology, key innovations, theoretical foundations, and practical implications.

**Example:** `/rml analyze-paper PRefLexOR`

### `compare-methods [method1] [method2] ...`
Compare multiple RLM approaches side-by-side, highlighting differences in architecture, computational efficiency, applicability domains, and trade-offs.

**Example:** `/rml compare-methods Mixture-of-Recursions Vertical-LoRA PRefLexOR`

### `design-recursive-system [domain]`
Design a recursive learning system for a specific application domain (e.g., scientific discovery, control systems, time series forecasting).

**Example:** `/rml design-recursive-system materials-science-discovery`

### `troubleshoot-convergence [issue-description]`
Diagnose convergence issues in recursive learning algorithms, providing theoretical explanations and practical solutions.

**Example:** `/rml troubleshoot-convergence error-propagation-through-deep-recursion-layers`

### `evaluate-applicability [use-case]`
Evaluate whether RLM approaches are suitable for a specific use case, recommending the most appropriate framework and identifying potential challenges.

**Example:** `/rml evaluate-applicability multi-modal-reasoning-vision-language-models`

## Process for Analysis

1. **Discovery**: Parse arguments to identify target(s) and operation type
2. **Literature Search**: Use WebFetch to retrieve 
