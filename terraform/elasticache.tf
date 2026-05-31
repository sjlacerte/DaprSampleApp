resource "aws_elasticache_subnet_group" "dapr_iot" {
  name       = "dapr-iot-redis-subnet-group"
  subnet_ids = module.vpc.private_subnets
}

resource "aws_security_group" "redis" {
  name        = "dapr-iot-redis-sg"
  description = "Allow Redis access from EKS nodes"
  vpc_id      = module.vpc.vpc_id

  ingress {
    from_port       = 6379
    to_port         = 6379
    protocol        = "tcp"
    security_groups = [module.eks.node_security_group_id]
  }

  tags = local.common_tags
}

resource "aws_elasticache_parameter_group" "dapr_iot" {
  name   = "dapr-iot-redis7"
  family = "redis7"

  parameter {
    name  = "notify-keyspace-events"
    value = "KEA"
  }

  tags = local.common_tags
}

resource "aws_elasticache_cluster" "dapr_iot" {
  cluster_id           = "dapr-iot-redis"
  engine               = "redis"
  node_type            = "cache.t3.micro" # smallest viable size
  num_cache_nodes      = 1
  parameter_group_name = aws_elasticache_parameter_group.dapr_iot.name
  engine_version       = "7.0"
  port                 = 6379
  subnet_group_name    = aws_elasticache_subnet_group.dapr_iot.name
  security_group_ids   = [aws_security_group.redis.id]

  tags = local.common_tags
}
