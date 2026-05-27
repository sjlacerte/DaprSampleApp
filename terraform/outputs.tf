output "cluster_name" {
  value = module.eks.cluster_name
}

output "cluster_endpoint" {
  value = module.eks.cluster_endpoint
}

output "kubeconfig_command" {
  value = "aws eks update-kubeconfig --region ${var.aws_region} --name ${module.eks.cluster_name}"
}

output "redis_host" {
  value     = "${aws_elasticache_cluster.dapr_iot.cache_nodes[0].address}:${aws_elasticache_cluster.dapr_iot.cache_nodes[0].port}"
  sensitive = false
}

output "ingestor_role_arn" {
  value = aws_iam_role.ingestor.arn
}

output "processor_role_arn" {
  value = aws_iam_role.processor.arn
}
