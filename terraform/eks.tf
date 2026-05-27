module "eks" {
  source  = "terraform-aws-modules/eks/aws"
  version = "~> 20.0"

  cluster_name    = var.cluster_name
  cluster_version = "1.30"

  vpc_id                         = module.vpc.vpc_id
  subnet_ids                     = module.vpc.private_subnets
  cluster_endpoint_public_access = true

  enable_irsa = true

  eks_managed_node_groups = {
    default = {
      min_size       = 2
      max_size       = 2
      desired_size   = 2
      instance_types = ["t3.small"]

      labels = local.common_tags
    }
  }

  tags = local.common_tags
}
