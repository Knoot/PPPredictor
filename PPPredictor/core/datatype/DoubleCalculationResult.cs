namespace PPPredictor.Core.DataType
{
    public class DoubleCalculationResult
    {
        public double Value { get; set; }
        public bool IsValid { get; set; }

        public DoubleCalculationResult(double value, bool isValid)
        {
            Value = value;
            IsValid = isValid;
        }
    }
}
