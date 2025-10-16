using System;

namespace HSIntel.Engine.Models
{
    public sealed class EvaluationWeights
    {
        public EvaluationWeights(
            double boardControl,
            double cardAdvantage,
            double healthDifferential,
            double manaCurve,
            double tempoGain,
            double lethalThreat)
        {
            BoardControl = boardControl;
            CardAdvantage = cardAdvantage;
            HealthDifferential = healthDifferential;
            ManaCurve = manaCurve;
            TempoGain = tempoGain;
            LethalThreat = lethalThreat;
        }

        public double BoardControl { get; }

        public double CardAdvantage { get; }

        public double HealthDifferential { get; }

        public double ManaCurve { get; }

        public double TempoGain { get; }

        public double LethalThreat { get; }
    }

    public sealed class EvaluationBreakdown
    {
        public EvaluationBreakdown(
            double boardControl,
            double cardAdvantage,
            double healthDifferential,
            double manaCurve,
            double tempoGain,
            double lethalThreat)
        {
            BoardControl = boardControl;
            CardAdvantage = cardAdvantage;
            HealthDifferential = healthDifferential;
            ManaCurve = manaCurve;
            TempoGain = tempoGain;
            LethalThreat = lethalThreat;
        }

        public double BoardControl { get; }

        public double CardAdvantage { get; }

        public double HealthDifferential { get; }

        public double ManaCurve { get; }

        public double TempoGain { get; }

        public double LethalThreat { get; }
    }

    public sealed class EvaluationResult
    {
        public EvaluationResult(double totalScore, EvaluationBreakdown breakdown)
        {
            TotalScore = totalScore;
            Breakdown = breakdown ?? throw new ArgumentNullException(nameof(breakdown));
        }

        public double TotalScore { get; }

        public EvaluationBreakdown Breakdown { get; }

        public bool IndicatesLethal => Breakdown.LethalThreat >= 1.0;
    }
}
