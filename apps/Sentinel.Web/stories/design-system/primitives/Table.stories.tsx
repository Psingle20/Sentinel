import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/design-system/primitives/table";

const observations = [
  { id: "obs-1", signal: "checkout SLO burn rate", value: "14.8x", time: "14:02:10" },
  { id: "obs-2", signal: "Redis connected clients", value: "9,842", time: "14:02:15" },
  { id: "obs-3", signal: "checkout-service deployment", value: "v2.4.1", time: "13:58:41" },
];

const meta = {
  title: "Primitives/Table",
  component: Table,
} satisfies Meta<typeof Table>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Observations: Story = {
  render: () => (
    <Table>
      <TableCaption>Observations linked to the incident.</TableCaption>
      <TableHeader>
        <TableRow>
          <TableHead>Signal</TableHead>
          <TableHead>Value</TableHead>
          <TableHead>Event time</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {observations.map((observation) => (
          <TableRow key={observation.id}>
            <TableCell>{observation.signal}</TableCell>
            <TableCell className="font-mono">{observation.value}</TableCell>
            <TableCell className="font-mono">{observation.time}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  ),
};

export const Empty: Story = {
  render: () => (
    <Table>
      <TableCaption>No observations are linked yet.</TableCaption>
      <TableHeader>
        <TableRow>
          <TableHead>Signal</TableHead>
          <TableHead>Value</TableHead>
          <TableHead>Event time</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody />
    </Table>
  ),
};
