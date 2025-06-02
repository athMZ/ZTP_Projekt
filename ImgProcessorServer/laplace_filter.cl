__kernel void laplace_filter(__global uchar* input, __global uchar* output, int width, int height, int stride)
{
	int x = get_global_id(0);
	int y = get_global_id(1);

	if (x <= 0 || x >= width - 1 || y <= 0 || y >= height - 1)
		return;

	int idx = y * stride + x * 4;
	for (int c = 0; c < 3; c++) {
		int center = input[idx + c] * -4;
		int left   = input[idx + c - 4];
		int right  = input[idx + c + 4];
		int top    = input[idx + c - stride];
		int bottom = input[idx + c + stride];
		int value = clamp(center + left + right + top + bottom, 0, 255);
		output[idx + c] = (uchar)value;
	}
	output[idx + 3] = 255; // alpha
}