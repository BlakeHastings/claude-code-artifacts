# /// script
# requires-python = ">=3.9"
# dependencies = ["click", "rich"]
# ///

"""
Refinement engine for recursive-exploration skill.
Implements self-critique loop, convergence detection, and strategy switching logic.
Handles iterative improvement cycles with quality gate evaluation.
"""

import click
from typing import Dict, List, Optional, Tuple
from dataclasses import dataclass, field
from rich.console import Console
from rich.table import Table


@dataclass
class QualityMetrics:
    """Quality metrics for output evaluation."""

    completeness_score: float = 0.0  # 0-1 scale
    accuracy_score: float = 0.0  # 0-1 scale
    clarity_score: float = 0.0  # 0-1 scale
    evidence_count: int = 0  # Number of source references
    reasoning_depth: int = 0  # Number of reasoning sections

    @property
    def overall_score(self) -> float:
        """Weighted average across all metrics."""
        return (
            self.completeness_score * 0.4
            + self.accuracy_score * 0.35
            + self.clarity_score * 0.25
        )


@dataclass
class CritiqueResult:
    """Results from self-critique analysis."""

    issues_found: List[str] = field(default_factory=list)
    improvement_potential: float = 0.0  # 0-1 scale
    severity: str = "low"  # low, medium, high


class ConvergenceDetector:
    """Detects when exploration has converged (no meaningful improvement)."""

    CONVERGENCE_THRESHOLD = 0.15  # <15% improvement triggers stop

    def __init__(self):
        self.iteration_history: List[QualityMetrics] = []
        self.convergence_detected: bool = False
        self.last_improvement: Optional[float] = None

    def add_iteration(self, metrics: QualityMetrics) -> Tuple[bool, str]:
        """Add iteration results and check for convergence."""
        self.iteration_history.append(metrics)

        if len(self.iteration_history) < 2:
            return False, "Need at least 2 iterations to detect convergence"

        # Calculate improvement from previous iteration
        prev_metrics = self.iteration_history[-2]
        current_metrics = metrics

        improvements = {
            "completeness": current_metrics.completeness_score
            - prev_metrics.completeness_score,
            "accuracy": current_metrics.accuracy_score - prev_metrics.accuracy_score,
            "clarity": current_metrics.clarity_score - prev_metrics.clarity_score,
        }

        avg_improvement = sum(improvements.values()) / len(improvements)
        self.last_improvement = avg_improvement

        # Check convergence threshold
        if abs(avg_improvement) < self.CONVERGENCE_THRESHOLD:
            self.convergence_detected = True

            reason = f"Improvement plateaued at {avg_improvement:.2%} (threshold: {self.CONVERGENCE_THRESHOLD:.0%})"

            # Additional convergence signals
            if len(self.iteration_history) >= 5 and avg_improvement < 0.10:
                reason += " + 5+ iterations completed without significant gain"

            return True, reason

        self.convergence_detected = False
        return False, f"Still improving by {avg_improvement:.2%}"


class QualityGates:
    """Evaluates quality gates before final output."""

    THRESHOLDS = {
        "source_diversity": 0.60,  # 60%+ of claims backed by multiple sources
        "alternatives_addressed": 2,  # At least 2 counterarguments addressed
        "edge_cases_mentioned": 2,  # At least 2 edge cases documented
        "reasoning_sections": 2,  # Minimum reasoning sections for traceability
    }

    @staticmethod
    def evaluate_answer(answer: str) -> Dict[str, float]:
        """Evaluate answer against quality gates."""

        metrics = {
            "source_diversity": QualityGates._score_source_diversity(answer),
            "alternatives_addressed": QualityGates._count_alternative_views(answer),
            "edge_cases_mentioned": QualityGates._count_edge_cases(answer),
            "reasoning_sections": QualityGates._count_reasoning_markers(answer),
        }

        # Normalize scores for comparison (all 0-1 scale except counts which stay as-is)
        normalized = {}
        for gate, value in metrics.items():
            threshold = QualityGates.THRESHOLDS[gate]
            if isinstance(threshold, int):
                # For count-based gates, normalize to 0-1 based on reaching threshold
                normalized[gate] = min(value / (threshold * 2), 1.0)
            else:
                normalized[gate] = value

        return normalized

    @staticmethod
    def _score_source_diversity(answer: str) -> float:
        """Score based on multiple source references."""
        # Count unique source mentions (approximation)
        import re

        sources = re.findall(r"\[(?:vault|web|codebase)[^\]]*\]", answer, re.IGNORECASE)
        return min(len(set(sources)) / 3.0, 1.0) if sources else 0.5

    @staticmethod
    def _count_alternative_views(answer: str) -> int:
        """Count alternative viewpoints addressed in answer."""
        # Look for counterargument indicators
        indicators = [
            r"however",
            r"though",
            r"albeit",
            r"on the other hand",
            r"alternatively",
            r"may not always be",
            r"important caveat",
            r"limitations include",
        ]

        count = 0
        for indicator in indicators:
            matches = re.findall(indicator, answer, re.IGNORECASE)
            count += len(matches)

        return max(count // 2, 0)  # At least one paragraph needed per counterargument

    @staticmethod
    def _count_edge_cases(answer: str) -> int:
        """Count edge case or limitation mentions."""
        indicators = [
            r"edge case",
            r"exception",
            r"limitation",
            r"important caveat",
            r"special case",
            r"only when",
            r"unless",
            r"may not apply to",
        ]

        count = 0
        for indicator in indicators:
            matches = re.findall(indicator, answer, re.IGNORECASE)
            count += len(matches)

        return max(count // 2, 0)

    @staticmethod
    def _count_reasoning_markers(answer: str) -> int:
        """Count explicit [[REASONING]] markers."""
        import re

        matches = re.findall(r"\[\[REASONING\]\].*?(?=\n\n|\Z)", answer, re.DOTALL)
        return len(matches)


class StrategySwitcher:
    """Handles switching exploration strategies when stuck."""

    STRATEGIES = ["breadth-first", "depth-first", "targeted", "hybrid"]

    def __init__(self):
        self.current_strategy_idx = 0
        self.switch_count = 0
        self.max_switches = 3

    def get_alternative_strategy(self) -> str:
        """Get next strategy in rotation."""
        if self.switch_count < self.max_switches - 1:
            self.current_strategy_idx = (self.current_strategy_idx + 1) % len(
                self.STRATEGIES
            )
            self.switch_count += 1
            return self.STRATEGIES[self.current_strategy_idx]

        return None  # No more strategies available

    def reset_switches(self):
        """Reset switch counter for fresh attempt."""
        self.switch_count = 0


@click.command()
def refine():
    """Main refinement command - orchestrates self-critique loop."""
    console = Console()

    click.echo(click.style("\n=== Refinement Engine ===", fg="green"))
    click.echo("Self-critique and convergence detection logic")


def generate_critique_response(answer: str, topic: str) -> Dict[str, List[str]]:
    """Generate self-critique of current answer."""

    critique = {
        "completeness": [],
        "accuracy": [],
        "clarity": [],
        "reasoning_quality": [],
    }

    # Analyze for completeness issues
    if len(answer.split("Section")) < 4:
        critique["completeness"].append(
            "Answer may lack sufficient depth - consider expanding key sections"
        )

    # Check for unsupported claims (approximation)
    unsupported = re.findall(
        r"I think|I believe|might be|could be", answer, re.IGNORECASE
    )
    if len(unsupported) > 3:
        critique["accuracy"].append(
            "Several claims use hedging language - consider strengthening with evidence"
        )

    # Check clarity issues
    long_sentences = [s for s in answer.split(".") if len(s.strip()) > 40]
    if len(long_sentences) > 5:
        critique["clarity"].append(
            "Some sentences are overly long - break them into shorter statements"
        )

    return critique


def calculate_improvement_score(
    current_metrics: QualityMetrics,
    previous_metrics: QualityMetrics,
    improvement_threshold: float = 0.15,
) -> Tuple[bool, float]:
    """Calculate overall improvement score between iterations."""

    improvements = {
        "completeness": current_metrics.completeness_score
        - previous_metrics.completeness_score,
        "accuracy": current_metrics.accuracy_score - previous_metrics.accuracy_score,
        "clarity": current_metrics.clarity_score - previous_metrics.clarity_score,
    }

    avg_improvement = sum(improvements.values()) / len(improvements)

    is_significant = abs(avg_improvement) >= improvement_threshold

    return is_significant, avg_improvement


def check_premature_convergence(
    metrics: QualityMetrics, iteration: int, max_iterations: int
) -> bool:
    """Check if we're stopping too early."""

    # Still making significant improvements?
    if metrics.overall_score > 0.75 and iteration < max_iterations * 0.4:
        return True

    # Quality gates not yet satisfied?
    quality = QualityGates.evaluate_answer("placeholder")  # Would use actual answer
    gates_passed = sum(1 for v in quality.values() if v >= 0.8)

    if gates_passed < len(QualityGates.THRESHOLDS) * 0.6:
        return True

    return False


def generate_refinement_suggestion(
    current_answer: str, critique: Dict[str, List[str]]
) -> str:
    """Generate suggestion for refining the answer based on critique."""

    suggestions = []

    if critique["completeness"]:
        suggestions.append(
            "Expand sections identified as incomplete with additional details and examples"
        )

    if critique["accuracy"]:
        suggestions.append(
            "Replace hedging language with stronger claims backed by specific evidence from sources"
        )

    if critique["clarity"]:
        suggestions.append(
            "Break long sentences into shorter, clearer statements for better readability"
        )

    return "; ".join(suggestions)


if __name__ == "__main__":
    refine()
