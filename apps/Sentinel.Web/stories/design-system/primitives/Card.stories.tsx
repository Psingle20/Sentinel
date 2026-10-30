import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { Button } from "@/design-system/primitives/button";
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/design-system/primitives/card";

const meta = {
  title: "Primitives/Card",
  component: Card,
} satisfies Meta<typeof Card>;

export default meta;

type Story = StoryObj<typeof meta>;

export const WithAllSlots: Story = {
  render: () => (
    <Card className="max-w-md">
      <CardHeader>
        <CardTitle>Roll back checkout-service</CardTitle>
        <CardDescription>From v2.4.1 to v2.4.0 in the reference environment.</CardDescription>
        <CardAction>
          <Button variant="outline" size="sm">Details</Button>
        </CardAction>
      </CardHeader>
      <CardContent>
        <p className="text-body text-text-muted">
          Expected effect: Redis client connections return below the scenario threshold within 90 seconds.
        </p>
      </CardContent>
      <CardFooter className="gap-2">
        <Button>Approve plan</Button>
        <Button variant="ghost">Reject</Button>
      </CardFooter>
    </Card>
  ),
};

export const ContentOnly: Story = {
  render: () => (
    <Card className="max-w-md">
      <CardContent>
        <p className="text-body">A card without header or footer.</p>
      </CardContent>
    </Card>
  ),
};
