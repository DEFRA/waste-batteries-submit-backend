#!/bin/bash
set -eu

# Local stand-in for the service topic. CDP creates the real topic from a Portal request.
# The queue lets a developer read the envelope. It is not the consumer.

topic_name="waste_batteries_submit_events"
queue_name="waste_batteries_submit_events_demo"

topic_arn="$(aws sns create-topic --name "$topic_name" --query TopicArn --output text)"
queue_url="$(aws sqs create-queue --queue-name "$queue_name" --query QueueUrl --output text)"
queue_arn="$(
    aws sqs get-queue-attributes \
        --queue-url "$queue_url" \
        --attribute-names QueueArn \
        --query "Attributes.QueueArn" \
        --output text
)"

cat > /tmp/waste-batteries-submit-queue-policy.json <<EOF
{
  "Policy": "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Effect\":\"Allow\",\"Principal\":\"*\",\"Action\":\"sqs:SendMessage\",\"Resource\":\"$queue_arn\",\"Condition\":{\"ArnEquals\":{\"aws:SourceArn\":\"$topic_arn\"}}}]}"
}
EOF

aws sqs set-queue-attributes \
    --queue-url "$queue_url" \
    --attributes file:///tmp/waste-batteries-submit-queue-policy.json

aws sns subscribe \
    --topic-arn "$topic_arn" \
    --protocol sqs \
    --notification-endpoint "$queue_arn" \
    --attributes RawMessageDelivery=true \
    --query SubscriptionArn \
    --output text

echo "Ready: $queue_name is subscribed to $topic_name ($topic_arn)"
