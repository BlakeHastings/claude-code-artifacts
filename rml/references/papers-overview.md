---
name: papers-overview
---
# RLM Papers Overview and Timeline

This document provides a comprehensive bibliography of Recursive Learning Models research from early stochastic foundations through modern LLM-based frameworks.
## Foundational Papers (2009-2010)

### Stochastic Recursive Learning in Finance
Laruelle, S., Lehalle, C.A., & Pages, G. (2009). Optimal split of orders across liquidity pools: a stochastic algorithm approach. arXiv:0910.1166

- Two stochastic recursive learning algorithms (one optimization-based, one reinforcement-learning based)
- Proved almost sure convergence under ergodic assumptions
- Demonstrated Central Limit Theorem behavior for i.i.d. inputs

### Optimized Recursive Neural Network Algorithm
Sha, D., & Bajic, V.B. (2010). An optimized recursive learning algorithm for three-layer feedforward neural networks for MIMO nonlinear system identifications. arXiv:1004.1997

- Avoids need to manually select proper learning rates
- Provides proof of weak convergence

## Modern LLM-Based Recursive Learning (2021-2025)

### RET-LLM: Initial Write-Read Memory Framework
Modarressi, A., Imani, A., Fayyaz, M., & Schutze, H. (2023). RET-LLM: Towards a General Read-Write Memory for Large Language Models. arXiv:2305.14322

- Novel framework equipping LLMs with general write-read memory unit
- Extracts and saves knowledge in form of triplets (inspired by Davidsonian semantics theory)
- Memory unit designed to be scalable, aggregatable, updatable, and interpretable

### PRefLexOR: Preference-Based Recursive Language Modeling
Buehler, M.J. (2024). PRefLexOR: Preference-based Recursive Language Modeling for Exploratory Optimization of Reasoning and Agentic Thinking. arXiv:2410.12375

- Combines preference optimization with reinforcement learning concepts for self-teaching through iterative reasoning improvements
- Multi-stage training: Stage 1 aligns model reasoning with accurate decision paths, Stage 2 enhances performance using rejection sampling
- Thinking token framework introduces iterative feedback loops for deeper coherence and consistency

### Graph-PReFLexOR: Enhanced Version with Graph Reasoning
Buehler, M.J. (2025). In-situ graph reasoning and knowledge expansion using Graph-PReFLexOR. arXiv:2501.08120

- Combines graph reasoning with symbolic abstraction for dynamic knowledge expansion
- Inspired by category theory - encodes concepts as nodes with relationships as edges

- Tasks yield structured outputs: knowledge graphs, abstract patterns, and final answers
- Applications: hypothesis generation in materials science, creative reasoning (e.g., discovering relationships between mythological concepts and materials science)

### Mixture-of-Recursions: Adaptive Computational Efficiency
Bae, S., Kim, Y., Bayat, R., et al. (2025). Mixture-of-Recursions: Learning Dynamic Recursive Depths for Adaptive Token-Level Computation. arXiv:2507.10524

### Mixture-of-Recursions: Adaptive Computational Efficiency
Bae, S., Kim, Y., Bayat, R., et al. (2025). Mixture-of-Recursions: Learning Dynamic Recursive Depths for Adaptive Token-Level Computation. arXiv:2507.10524

- Unified recursive transformer framework sharing single stack of layers across recursion steps
- Lightweight routers dynamically assign different recursion depths to individual tokens

### Vertical LoRA: EM Interpretation of Transformers
Fu, Z. (2024). Vertical LoRA: Dense Expectation-Maximization Interpretation of Transformers. arXiv:2406.09315

- Interprets Transformers as dense Expectation-Maximization algorithms performed on Bayesian Networks
- Each layer recursively learns an increment based on previous layer computations

- Applies LoRA (Low-Rank Adaptation) decomposition to the increments orthogonal to standard LoRA
- Dramatically reduces parameter count while preserving model performance

## Control Systems Applications (2023-2024)

### Data-Driven Discovery with Recursive Representation
Tiwari, M., Nehma, G., & Lusch, B. (2023). Computationally Efficient Data-Driven Discovery and Linear Representation of Nonlinear Systems For Control. arXiv:2309.04074

- Koopman operator theory framework for system identification
- Deep learning with recursive learning for linearization of nonlinear systems

### Stability-Certified On-Policy Data-Driven LQR
Sforni, L., Carnevale, G., Notarnicola, I., & Notarstefano, G. (2024). Stability-Certified On-Policy Data-Driven LQR via Recursive Learning and Policy Gradient. arXiv:2403.05367

- Relearn LQR framework combining recursive least squares with direct policy search
- Lyapunov-based stability guarantees using averaging and timescale separation theories
- Applied to aircraft control with both static and drifting parameters

## Domain-Specific Applications (2021-2024)

### Medical Imaging: Recursive 3D Segmentation
He, X., Tan, C.W., Tan, V., & Li, K. (2022). Recursive 3D Segmentation of Shoulder Joint with Coarse-scanned MR Image. arXiv:2203.07846

- Iteratively uses generated labels to reduce segmentation errors
- Increases training dataset for next iteration

### Time Series: Transfer-Recursive Ensemble Learning for COVID Prediction
Chakraborty, D., Goswami, D., Ghosh, S., et al. (2021). Transfer-Recursive-Ensemble Learning for Multi-Day COVID-19 Prediction in India using Recurrent Neural Networks. arXiv:2108.09131

- Pre-trained GRU models on data from four countries (USA, Brazil, Spain, Bangladesh)
- Transfer learning to India's dataset with retraining

### State-Space Models: Recursive Variational Inference
Mastrototaro, A., Muller, M., & Olsson, J. (2024). Recursive Learning of Asymptotic Variational Objectives. arXiv:2411.02217

- Enables online variational inference in State-Space Models for streaming data
- Maximizes IWAE-type variational lower bound on asymptotic contrast function using stochastic approximation

## Key Findings Summary

- **Convergence Guarantees**: Multiple papers provide mathematical proofs for convergence under various assumptions (ergodicity, i.i.d.)

- **Stability Analysis**: Lyapunov-based approaches enable formal stability certificates for learning-control systems
- **Computational Efficiency**: MoR demonstrates quadratic attention computation can be focused only on active tokens, improving efficiency by orders of magnitude

- **Small Model Performance**: PRefLexOR shows 3B-parameter models can achieve reasoning capabilities typically requiring much larger models
- **Parameter Efficiency**: VLoRA achieves dramatic parameter reduction while preserving performance through recursive layer design

- **Cross-Domain Adaptation**: Transfer-recursive learning enables effective knowledge transfer across different contexts and domains

---
name: methodologies-reference
---
