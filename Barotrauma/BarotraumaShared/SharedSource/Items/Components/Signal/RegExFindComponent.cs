using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Barotrauma.Items.Components
{
    class RegExFindComponent : ItemComponent
    {
        /// <summary>
        /// The timeout should be a lot shorter (used to be 1 ms), but there seems to be an issue in .NET 8 
        /// that sometimes causes the evaluation to randomly take a significant amount of time 
        /// (an expression that normally takes 20 ticks might sometimes take several milliseconds every few minutes).
        /// So let's use a relatively long timeout instead, and measure the actual time the expression took ourselves.
        /// </summary>
        private static readonly TimeSpan timeout = TimeSpan.FromMilliseconds(50);

        private static readonly TimeSpan shortTimeout = TimeSpan.FromMilliseconds(1);

        private readonly Stopwatch stopwatch = new Stopwatch();
        private bool timedOut;
        private int timeOutsInARow;
        const int MaxTimeOutsInARow = 3;

        private string expression;

        private string receivedSignal;
        private string previousReceivedSignal;

        private bool previousResult;
        private GroupCollection previousGroups;

        private Regex regex;

        private bool nonContinuousOutputSent;


        private int maxOutputLength;
        [Editable, Serialize(200, IsPropertySaveable.No, description: "The maximum length of the output string. Warning: Large values can lead to large memory usage or networking issues.")]
        public int MaxOutputLength
        {
            get { return maxOutputLength; }
            set
            {
                maxOutputLength = Math.Max(value, 0);
            }
        }

        private string output;

        [InGameEditable, Serialize("1", IsPropertySaveable.Yes, description: "The signal this item outputs when the received signal matches the regular expression.", alwaysUseInstanceValues: true)]
        public string Output 
        {
            get { return output; }
            set
            {
                if (value == null) { return; }
                output = value;
                if (output.Length > MaxOutputLength && (item.Submarine == null || !item.Submarine.Loading))
                {
                    output = output.Substring(0, MaxOutputLength);
                }
            }
        }

        [InGameEditable, Serialize(false, IsPropertySaveable.Yes, description: "Should the component output a value of a capture group instead of a constant signal.", alwaysUseInstanceValues: true)]
        public bool UseCaptureGroup { get; set; }

        [InGameEditable, Serialize(false, IsPropertySaveable.Yes, description: "Should the component output the value of a capture group even if it's empty?", alwaysUseInstanceValues: true)]
        public bool OutputEmptyCaptureGroup { get; set; }

        [InGameEditable, Serialize("0", IsPropertySaveable.Yes, description: "The signal this item outputs when the received signal does not match the regular expression.", alwaysUseInstanceValues: true)]
        public string FalseOutput { get; set; }

        [InGameEditable, Serialize(true, IsPropertySaveable.Yes, description: "Should the component keep sending the output even after it stops receiving a signal, or only send an output when it receives a signal.", alwaysUseInstanceValues: true)]
        public bool ContinuousOutput { get; set; }

        [InGameEditable, Serialize("", IsPropertySaveable.Yes, description: "The regular expression used to check the incoming signals.", alwaysUseInstanceValues: true)]
        public string Expression
        {
            get { return expression; }
            set 
            {
                if (expression == value) { return; }
                expression = value;
                previousReceivedSignal = "";
                try
                {
                    regex = new Regex(
                        @expression,
                        options: RegexOptions.None,
                        matchTimeout: timeout);
                }
                catch
                {
                    return;
                }
                //reactivate the component, in case some faulty/malicious expression caused it to time out and deactivate itself
                timedOut = false;
            }
        }

        public RegExFindComponent(Item item, ContentXElement element)
            : base(item, element)
        {
            nonContinuousOutputSent = true;
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (timedOut)
            {
                item.SendSignal("TIMEOUT", "signal_out");
                return;
            }

            if (string.IsNullOrWhiteSpace(expression) || regex == null) { return; }
            if (!ContinuousOutput && nonContinuousOutputSent) { return; }

            if (receivedSignal != previousReceivedSignal && receivedSignal != null)
            {
                try
                {
                    stopwatch.Restart();
                    Match match = regex.Match(receivedSignal);
                    stopwatch.Stop();

                    //workaround to regex timeout issues in .NET 8, see comment on the timeout variable
                    if (stopwatch.Elapsed > shortTimeout)
                    {
                        timeOutsInARow++;
                        //if the regex times out just once every now and then, it's a symptom of the .NET 8 bug,
                        //if multiple times in a row, it's most likely a performance-intensive/malicious expression we should react to
                        if (timeOutsInARow >= MaxTimeOutsInARow)
                        {
                            throw new RegexMatchTimeoutException();
                        }
                    }
                    else
                    {
                        timeOutsInARow = 0;
                    }

                    previousResult = match.Success;
                    previousGroups = UseCaptureGroup && previousResult ? match.Groups : null;
                    previousReceivedSignal = receivedSignal;
                }
                catch (Exception e)
                {
                    if (e is RegexMatchTimeoutException)
                    {
                        timedOut = true;
                    }
                    else
                    {
                        item.SendSignal("ERROR", "signal_out");
                    }
                    previousResult = false;
                    return;
                }
            }

            string signalOut;
            bool allowEmptyStringOutput = false;
            if (previousResult)
            {
                if (UseCaptureGroup)
                {
                    if (previousGroups != null && previousGroups.TryGetValue(Output, out Group group))
                    {
                        signalOut = group.Value;
                        allowEmptyStringOutput = OutputEmptyCaptureGroup;
                    }
                    else
                    {
                        signalOut = FalseOutput;
                    }
                }
                else
                {
                    signalOut = Output;
                }
            }
            else
            {
                signalOut = FalseOutput;
            }

            if (!string.IsNullOrEmpty(signalOut) || (allowEmptyStringOutput && signalOut == string.Empty)) { item.SendSignal(signalOut, "signal_out"); }
            if (!ContinuousOutput)
            {
                nonContinuousOutputSent = true;
            }
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    receivedSignal = signal.value;
                    nonContinuousOutputSent = false;
                    break;
                case "set_output":
                    Output = signal.value;
                    break;
            }
        }
    }
}
