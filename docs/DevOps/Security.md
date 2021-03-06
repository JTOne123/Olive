# Securing your production environment with AWS

### Introduction
This document describes the process of deploying the application from the *development* environment to *production*.

Each microservice is treated as a separate project and the instructions on this document will be the same for all of them.

### Development
Almost every project these days will use GIT (or some other version control) to keep track of, and manage code changes. This document will assume that you use `Bitbucket`, but you can use any other repository provider as well.

For each application, we will create a project in Bitbucket. For each microservice in the application, we will create a separate repository in the project.

Developers will have write access only to the repositories of the microservices they are assigned to. 

#### _Security Caution_
You must NEVER store any sensitive information in the source code. Secure info such as connection strings, credentials, api keys etc, are injected to the application during the `deployment process`, which will be explained later in this article. 

### Continious Delivery
Our continious delivery and Jenkins build structure and processes have been described in details [here](https://github.com/Geeksltd/Olive/blob/master/docs/Microservices/DevOps/Jenkins.md).

### Jenkins structure
For our current Jenkins structure, we have two servers:

- **Windows VM**: to run Jenkins on, and use it as the Windows build slave.
- **Linux VM**: to build our docker images.

#### Isolasion of environments: Build vs Production
Our goal is to create the build environment in secure way that is only accessible from the company's network. It is important to give the Jenkins servers the permission they need to build and deploy the code, and nothing more.

In other words, we want to keep the production environment away from the build environment. To achive that we have created a `separate VPC (Virtual Private Cloud)` on AWS, which hosts the Jenkins servers.

#### Restricted access to Build environment
The two Jenkins servers share a `security group` that has been updated to give access to port 80 to requests initiated from our local company network (IP address). That way, people from outside our company network cannot access the Jenkins instance.

#### Secure deployment info
Jenkins jobs require certain secure information to build and deploy the code. Those are stored as `Jenkins' Credentials` through the Jenkins UI in the admin section. Such secure information include:

- Git repository credentials
- ECR repository credentials
- Kubernetes api credentials
- ...

> TODO: We can use IAM Roles to give access to the build server to push images to ECR to avoid having to store ECR credentials on Jenkins. Also, other secure info can be put on the AWS Secrets Manager and pulled by Jenkins when building to again avoid storing credentials on Jenkins.

### Container Registry
The container images, generated by our build process, should be stored in a `Container Registry` so that they are accissible by our production environment to pull and run. 

There are various `Container Registry` services to host Docker images, such as Docker hub, Google container registry or AWS ECR. 
We chose `AWS ECR` for two reasons: 

- Our build and production environment are already hosted on AWS. Therefore communication between them and the container registry will be faster.
- We can manage access permissions using `AWS IAM Roles` as opposed to storing and using sensitive credentials. 

For each microservice, we have created a separate container repository in the `eu-west-1` region. 

### Runtime Secure info
As mentioned earlier, we should avoid putting secure information in the code. One of the challenges we had was to find a way to securely pass sensitive information to the application in a way that cannot be hacked easily. 

#### AWS IAM
AWS offers the `IAM service` for managing access permissions to other AWS resources. Using IAM, you can tailor a `Role` for each service with the minimum permissions required. Since IAM roles are defined and assigned deep in the AWS infrastructure (using some hardware tricks) there is no need to deal with generating and storing credentials.

#### IAM Role for Kubernetes
IAM roles can only be assigned to AWS resources, hence Kubernetes currently only supports assigning them to nodes (i.e. Servers). 

**However, assigning a role to a node means that all container processes running on that node will share the same role. That is not ideal, as the node may host different pods which require different permissions. And we do not want have a super role in the system and share it between pods.**

There are some complex workaround solutions suggested by the community to assign a role to a pod but we didn’t want to add more elements to our cluster in order to keep it simple. So we came up with a solution so that we can assign IAM roles directly to pods without having the overhead of using other services on Kubernetes.

#### AWS IAM Role for Pods
As mentioned earlier, pods run on nodes and each node has a role on AWS, therefore the running pods share the same role as the hosting node.

- There is a feature in AWS IAM, which enables a user or a resource with an IAM role to `assume another role`, by creating a trust relationship between the assuming and assumed roles.
- Another feature that AWS IAM offers is to generate `temporary credentials` for each role. 

Using these two features, we managed to assign the `Service IAM role` that we created before, to our Kubernetes pod. For that, we had to create a `trust relationship` between the `node IAM role` and the `service IAM role`.

To assign the service IAM role to each pod, we added a new `environment variable` to the `service deployment template` whose value is read from a `Kubernetes secret` that we created for the service.

When a pod starts, the application reads the `service IAM role` from the environment variable. It then uses the AWS sdk to *assume* the service role, and then generates a `temporary credentials` for the service role. The lifetime of that credentials is a few hours, and there is a scheduled background process in the application to renew that regularly. The temporary credentials are stored in memory, and not persisted anywhere, which makes them more secure.

## Debugging the live AWS environment locally
There are times when you need to connect to the production environment while running the app on your development machine. For example, this is useful to identify some integration or data related issues that are hard or impossible to replicate in the dev environment.

To achieve that, follow these steps:

1. In `Website1\Properties\launchSettings.json` file, change `Development` to `Production`
1. Log on to the AWS Console website.
   - Under `IAM` select `Users` and then locate your developer or root admin user.
   - Under `Security credentials` generate an access key pair.
1. Open `Startup.cs` file and locate the constructor.
   - Replace the line `config.LoadAwsIdentity()` with `config.LoadAwsDevIdentity("my-access-key", "my-secret", loadSecrets: true);` using the access key details that you just created.
1. In `Startup.cs` locate `ConfigureAuthCookie` and comment out `options.DataProtectionProvider = new KmsDataProtectionProvider();`

If your user does not have access to the admin user, alternatively you can do the following:
1. Find your `user's ARN` and make a note of it.
1. Under `IAM Roles` locate the `{MyApplication}Runtime` role (the identify of your application at runtime).
1. Create a trust relationship between that role and your user.


#### Beware
- Never commit or push these changes to GIT. It can cause serious security issues.
- As soon as your debugging session is completed, remove the access key / secret from the code, and delete it from AWS also.
