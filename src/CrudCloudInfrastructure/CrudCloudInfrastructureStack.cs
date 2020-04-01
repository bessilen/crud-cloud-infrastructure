using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ECS.Patterns;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Lambda.EventSources;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SQS;

namespace CrudCloudInfrastructure
{
    public class CrudCloudInfrastructureStack : Stack
    {
        internal CrudCloudInfrastructureStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            var vpc = CreateVpc();

            var cluster = CreateCluster(vpc);

            var ec2Service = CreateService(cluster);

            var dynamoTitlesTable = CreateDynamoTitlesTable();

            var lambdaBucket = Bucket.FromBucketName(this, "dev-titles-synchronisation-lambda-bucket", "synchronisation-lambda-bucket");
            var titlesSynchronisationQueue = CreateTitlesSynchronisationQueue();

            var titlesSynchronisationLambda = CreateTitlesSynchrnisationLambda(vpc, lambdaBucket);
            titlesSynchronisationLambda.AddEventSource(new SqsEventSource(titlesSynchronisationQueue));

            var titlesBackupBucket = CreateTitlesBackupBucket();

            dynamoTitlesTable.GrantReadWriteData(ec2Service.TaskDefinition.TaskRole);
            titlesBackupBucket.GrantReadWrite(titlesSynchronisationLambda);
            titlesSynchronisationQueue.GrantSendMessages(ec2Service.TaskDefinition.TaskRole);
        }

        private Queue CreateTitlesSynchronisationQueue()
        {
            return new Queue(this, "dev-titles-synchronisation-queue", new QueueProps
            {
                QueueName = "titles-synchronisation-queue",
            });
        }

        private Bucket CreateTitlesBackupBucket()
        {
            return new Bucket(this, "dev-titles-backup-bucket", new BucketProps
            {
                BucketName = "titles-backup-bucket",
                RemovalPolicy = RemovalPolicy.DESTROY,
            });
        }

        private Function CreateTitlesSynchrnisationLambda(Vpc vpc, IBucket lambdaBucket)
        {
            return new Function(this, "dev-titles-synchronisation-lambda", new FunctionProps
            {
                Runtime = Runtime.DOTNET_CORE_2_1,
                Handler = "S3BackupFunction::S3BackupFunction.Function::FunctionHandler",
                Code = Code.FromBucket(lambdaBucket, "S3BackupFunction.zip"),
                Timeout = Duration.Seconds(30),
                Vpc = vpc,
            });
        }

        private Table CreateDynamoTitlesTable()
        {
            return new Table(this, "dev-titles-table", new TableProps()
            {
                TableName = "dev-titles-table",
                PartitionKey = new Attribute
                {
                    Name = "isbn",
                    Type = AttributeType.STRING,
                },
                RemovalPolicy = RemovalPolicy.DESTROY,
            });
        }

        private Vpc CreateVpc()
        {
            var vpc = new Vpc(this, "vpc-dev-aws-sandbox", new VpcProps
            {
                MaxAzs = 2,
                SubnetConfiguration = new []
                {
                    new SubnetConfiguration
                    {
                        Name = "dev-isolated-subnet",
                        SubnetType = SubnetType.ISOLATED,
                    }
                },
                NatGateways = 0,
            });

            vpc.AddInterfaceEndpoint("ecr-docker-endpoint", new InterfaceVpcEndpointOptions
            {
                Service = InterfaceVpcEndpointAwsService.ECR_DOCKER,
            });

            vpc.AddInterfaceEndpoint("sqs-endpoint", new InterfaceVpcEndpointOptions
            {
                Service = InterfaceVpcEndpointAwsService.SQS,
            });

            vpc.AddGatewayEndpoint("s3-endpoint", new GatewayVpcEndpointOptions
            {
                Service = GatewayVpcEndpointAwsService.S3,
            });

            vpc.AddGatewayEndpoint("dynamo-endpoint", new GatewayVpcEndpointOptions
            {
                Service = GatewayVpcEndpointAwsService.DYNAMODB,
            });

            return vpc;
        }

        private Cluster CreateCluster(Vpc vpc)
        {
            var cluster = new Cluster(this, "cluster-dev-aws-sandbox", new ClusterProps
            {
                Vpc = vpc,
                ClusterName = "dev-cluster",
                Capacity = new AddCapacityOptions
                {
                    DesiredCapacity = 1,
                    InstanceType = new InstanceType("t2.micro")
                },
            });
            return cluster;
        }

        private ApplicationLoadBalancedEc2Service CreateService(Cluster cluster)
        {
            var repository = Repository.FromRepositoryAttributes(this, "dev-api-repo", new RepositoryAttributes
            {
                RepositoryName = "app-repo",
                RepositoryArn = "arn:aws:ecr:us-east-1:714871639201:repository/app-repo",
            });

            return new ApplicationLoadBalancedEc2Service(this, "dev-ecs-service",
                new ApplicationLoadBalancedEc2ServiceProps()
                {
                    ServiceName = "dev-crud-api-service",
                    Cluster = cluster,
                    DesiredCount = 1,
                    TaskImageOptions = new ApplicationLoadBalancedTaskImageOptions
                    {
                        Image = ContainerImage.FromEcrRepository(repository, "latest"),
                    },
                    MemoryLimitMiB = 256,
                    PublicLoadBalancer = true,
                });
        }
    }
}
