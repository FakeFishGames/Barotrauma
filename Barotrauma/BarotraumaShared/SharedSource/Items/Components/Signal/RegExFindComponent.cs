﻿using System;
using System.Text.RegularExpressions;

namespace Barotrauma.Items.Components
{
    class RegExFindComponent : ItemComponent
    {
        private static readonly TimeSpan timeout = TimeSpan.FromMilliseconds(1);

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
                IsActive = true;
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
            if (string.IsNullOrWhiteSpace(expression) || regex == null) { return; }
            if (!ContinuousOutput && nonContinuousOutputSent) { return; }

            if (receivedSignal != previousReceivedSignal && receivedSignal != null)
            {
                try
                {
                    Match match = regex.Match(receivedSignal);
                    previousResult = match.Success;
                    previousGroups = UseCaptureGroup && previousResult ? match.Groups : null;
                    previousReceivedSignal = receivedSignal;
                }
                catch (Exception e)
                {
                    if (e is RegexMatchTimeoutException)
                    {
                        item.SendSignal("TIMEOUT", "signal_out");
                        //deactivate the component if the expression caused it to time out
                        IsActive = false;
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
