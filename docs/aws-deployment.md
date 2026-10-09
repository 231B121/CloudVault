# CloudVault - AWS Production Deployment Guide

This guide walks through configuring and deploying CloudVault to Amazon Web Services using **Amazon S3**, **Amazon RDS for SQL Server**, **AWS ECS Fargate**, **Application Load Balancer (ALB)**, and **Amazon CloudWatch**.

---

## 1. Prerequisites
- AWS CLI configured with administrator privileges (`aws configure`)
- Docker installed for container image builds
- Amazon Route 53 domain name and ACM SSL Certificate (for HTTPS)

---

## 2. Step 1: Create Amazon S3 Bucket
Create a private S3 bucket in your target region:
```bash
aws s3api create-bucket \
    --bucket cloudvault-production-storage-unique-id \
    --region us-east-1
```

### Enable Default Encryption (AES256)
```bash
aws s3api put-bucket-encryption \
    --bucket cloudvault-production-storage-unique-id \
    --server-side-encryption-configuration '{
        "Rules": [{
            "ApplyServerSideEncryptionByDefault": {
                "SSEAlgorithm": "AES256"
            }
        }]
    }'
```

### Block All Public Access
```bash
aws s3api put-public-access-block \
    --bucket cloudvault-production-storage-unique-id \
    --public-access-block-configuration '{
        "BlockPublicAcls": true,
        "IgnorePublicAcls": true,
        "BlockPublicPolicy": true,
        "RestrictPublicBuckets": true
    }'
```

### Apply Bucket Policy
Apply the bucket policy from `deployment/aws/s3-bucket-policy.json` to enforce TLS:
```bash
sed -i 's/${BUCKET_NAME}/cloudvault-production-storage-unique-id/g' deployment/aws/s3-bucket-policy.json
aws s3api put-bucket-policy \
    --bucket cloudvault-production-storage-unique-id \
    --policy file://deployment/aws/s3-bucket-policy.json
```

---

## 3. Step 2: Configure Amazon RDS for SQL Server
1. Launch an Amazon RDS instance for SQL Server (Express, Web, or Standard Edition).
2. Configure security group allowing inbound port `1433` only from your ECS VPC/Security Group.
3. Note your database endpoint: `cloudvault-db.cxxxx.us-east-1.rds.amazonaws.com`.

---

## 4. Step 3: Run Database Migrations
You can apply migrations using EF Core CLI or by executing `database/init.sql`:

### Option A: EF Core Command Line
```bash
export ConnectionStrings__DefaultConnection="Server=cloudvault-db.cxxxx.us-east-1.rds.amazonaws.com,1433;Database=CloudVaultDb;User Id=cloudvault_admin;Password=YourComplexPassword!;TrustServerCertificate=False;Encrypt=True;"
dotnet ef database update --project src/CloudVault.Infrastructure/CloudVault.Infrastructure.csproj --startup-project src/CloudVault.API/CloudVault.API.csproj
```

### Option B: Standalone SQL Script
Execute `database/init.sql` directly on the RDS instance using SQL Server Management Studio (SSMS), Azure Data Studio, or `sqlcmd`.

---

## 5. Step 4: Create IAM Task Role & Task Execution Role
1. Create ECS Task Execution Role (`ecsTaskExecutionRole`) for pulling ECR images and logging to CloudWatch.
2. Create Application Task Role using `deployment/aws/iam-policy.json`:
```bash
aws iam create-role \
    --role-name cloudvault-app-task-role \
    --assume-role-policy-document '{
        "Version": "2012-10-17",
        "Statement": [{
            "Effect": "Allow",
            "Principal": { "Service": "ecs-tasks.amazonaws.com" },
            "Action": "sts:AssumeRole"
        }]
    }'

aws iam put-role-policy \
    --role-name cloudvault-app-task-role \
    --policy-name CloudVaultS3AndLogsAccess \
    --policy-document file://deployment/aws/iam-policy.json
```

---

## 6. Step 5: Build and Push Docker Image to Amazon ECR
```bash
# 1. Authenticate with ECR
aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin <YOUR_ACCOUNT_ID>.dkr.ecr.us-east-1.amazonaws.com

# 2. Create repository
aws ecr create-repository --repository-name cloudvault

# 3. Build container
docker build -f deployment/Dockerfile -t cloudvault:latest .

# 4. Tag and push
docker tag cloudvault:latest <YOUR_ACCOUNT_ID>.dkr.ecr.us-east-1.amazonaws.com/cloudvault:latest
docker push <YOUR_ACCOUNT_ID>.dkr.ecr.us-east-1.amazonaws.com/cloudvault:latest
```

---

## 7. Step 6: Deploy with CloudFormation or AWS Copilot
A complete CloudFormation template is provided in `deployment/aws/cloudformation-template.yaml`.
Deploy it with a single command:
```bash
aws cloudformation deploy \
    --template-file deployment/aws/cloudformation-template.yaml \
    --stack-name cloudvault-prod \
    --parameter-overrides \
        EnvironmentName=prod \
        DBMasterUsername=cloudvault_admin \
        DBMasterPassword="YourStrongPassword123!" \
    --capabilities CAPABILITY_IAM CAPABILITY_NAMED_IAM
```

---

## 8. Step 7: CloudWatch Logging & Alarms
CloudVault sends structured logs to `/aws/cloudvault/api` in Amazon CloudWatch.
To monitor storage health:
1. Navigate to **CloudWatch > Log Groups > `/aws/cloudvault/api`**.
2. Create Metric Filters on warning events like `"Storage quota exceeded"` or `"AWS S3 error"`.
3. Set up an Amazon SNS alert for automated notifications.
