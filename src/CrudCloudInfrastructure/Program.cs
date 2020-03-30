using Amazon.CDK;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CrudCloudInfrastructure
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();
            new CrudCloudInfrastructureStack(app, "dev-aws-sandbox-stack");
            app.Synth();
        }
    }
}
